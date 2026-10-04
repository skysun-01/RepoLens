using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Application.Common.Abstractions.Messaging;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Abstractions.Security;
using RepoLens.Application.Common.Errors;
using RepoLens.Application.Summaries;
using RepoLens.Application.Summaries.Processing;
using RepoLens.Application.Users;
using RepoLens.Domain.Repos;
using RepoLens.Domain.Summaries;
using RepoLens.Domain.Users;

namespace RepoLens.UnitTests.Application;

public sealed class SummaryProcessingTests
{
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly ISummaryJobRepository _jobs = Substitute.For<ISummaryJobRepository>();
    private readonly IRepoRepository _repos = Substitute.For<IRepoRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITokenProtector _protector = Substitute.For<ITokenProtector>();
    private readonly IMessagePublisher _publisher = Substitute.For<IMessagePublisher>();
    private readonly User _user;
    private readonly Repo _repo;
    private readonly SummaryJob _job;

    public SummaryProcessingTests()
    {
        _user = User.Register(1, "octo", null, null, null, "encrypted", "repo", TestData.Now);
        _repo = Repo.Create(_user.Id, TestData.Details(), TestData.Now);
        _job = SummaryJob.Create(_user.Id, _repo.Id, _repo.FullName, "main", TestData.Sha, force: false, TestData.Now);

        _jobs.GetAsync(_job.Id, Arg.Any<CancellationToken>()).Returns(_job);
        _repos.GetAsync(_user.Id, _repo.Id, Arg.Any<CancellationToken>()).Returns(_repo);
        _users.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _protector.Unprotect("encrypted").Returns("gho_token");
    }

    [Fact]
    public async Task Successful_run_completes_the_job_and_publishes_an_event()
    {
        await Processor(new FakeStep(succeed: true)).HandleAsync(Command(), Delivery(1), CancellationToken.None);

        Assert.Equal(SummaryStatus.Completed, _job.Status);
        Assert.NotNull(_job.Pdf);
        await _publisher.Received(1).PublishAsync(Arg.Is<SummaryCompletedEvent>(e => e.JobId == _job.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Permanent_error_fails_the_job_without_retrying()
    {
        var step = new FakeStep(error: new GitHubNotFoundException("octo/shop"));

        await Processor(step).HandleAsync(Command(), Delivery(1), CancellationToken.None);

        Assert.Equal(SummaryStatus.Failed, _job.Status);
        Assert.Equal("github_not_found", _job.Error?.Code);
        await _publisher.Received(1).PublishAsync(Arg.Is<SummaryFailedEvent>(e => e.ErrorCode == "github_not_found"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Transient_error_returns_the_job_to_the_queue_and_rethrows_for_redelivery()
    {
        var step = new FakeStep(error: new GitHubUnavailableException("down"));

        await Assert.ThrowsAsync<GitHubUnavailableException>(() =>
            Processor(step).HandleAsync(Command(), Delivery(2), CancellationToken.None));

        Assert.Equal(SummaryStatus.Queued, _job.Status);
        Assert.Equal("github_unavailable", _job.Error?.Code);
    }

    [Fact]
    public async Task Transient_error_on_the_last_delivery_fails_the_job_and_rethrows_for_dead_lettering()
    {
        var step = new FakeStep(error: new TimeoutException("slow"));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            Processor(step).HandleAsync(Command(), Delivery(5), CancellationToken.None));

        Assert.Equal(SummaryStatus.Failed, _job.Status);
        Assert.Equal("processing_error", _job.Error?.Code);
    }

    [Fact]
    public async Task Duplicate_delivery_of_a_finished_job_is_ignored()
    {
        _job.Fail(new JobError("x", "y"), TestData.Now);
        var step = new FakeStep(succeed: true);

        await Processor(step).HandleAsync(Command(), Delivery(1), CancellationToken.None);

        Assert.False(step.Ran);
        await _jobs.DidNotReceive().UpdateAsync(Arg.Any<SummaryJob>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Losing_the_claim_to_another_worker_skips_the_job()
    {
        _jobs.UpdateAsync(_job, Arg.Any<CancellationToken>()).Throws(new ConcurrencyException("SummaryJob", _job.Id));
        var step = new FakeStep(succeed: true);

        await Processor(step).HandleAsync(Command(), Delivery(1), CancellationToken.None);

        Assert.False(step.Ran);
    }

    [Fact]
    public async Task Pipeline_runs_steps_in_order_and_records_each_stage()
    {
        var recorded = new List<(SummaryStage Stage, int Progress)>();
        await _jobs.SaveProgressAsync(Arg.Do<SummaryJob>(j => recorded.Add((j.Stage, j.Progress))), Arg.Any<CancellationToken>());

        var order = new List<string>();
        var first = new RecordingStep(100, SummaryStage.ResolvingCommit, 5, order, reportHalfway: false);
        var second = new RecordingStep(200, SummaryStage.IndexingCode, 20, order, reportHalfway: true);
        var pipeline = new SummaryPipeline([second, first], _jobs, _time);

        _job.Start(TestData.Now, TimeSpan.FromMinutes(15));
        await pipeline.RunAsync(new SummaryPipelineContext(_job, _repo, "token"), CancellationToken.None);

        Assert.Equal(["ResolvingCommit", "IndexingCode"], order);
        Assert.Equal((SummaryStage.ResolvingCommit, 5), recorded[0]);
        Assert.Equal((SummaryStage.IndexingCode, 20), recorded[1]);
        Assert.Equal((SummaryStage.IndexingCode, 59), recorded[2]);
    }

    private SummaryJobProcessor Processor(params ISummaryPipelineStep[] steps) => new(
        _jobs,
        _repos,
        new GitHubTokenAccessor(_users, _protector),
        new SummaryPipeline(steps, _jobs, _time),
        _publisher,
        TestData.Options(new SummaryProcessingOptions()),
        _time,
        NullLogger<SummaryJobProcessor>.Instance);

    private GenerateSummaryCommand Command() => GenerateSummaryCommand.For(_job);

    private static MessageContext Delivery(int count) => new("message-id", count, MaxDeliveryCount: 5);

    private sealed class FakeStep(bool succeed = false, Exception? error = null) : ISummaryPipelineStep
    {
        public bool Ran { get; private set; }

        public int Order => 1;

        public SummaryStage Stage => SummaryStage.GeneratingSummary;

        public int StartProgress => 10;

        public Task ExecuteAsync(SummaryPipelineContext context, CancellationToken cancellationToken)
        {
            Ran = true;
            if (error is not null)
            {
                throw error;
            }

            if (succeed)
            {
                context.Summary = new RepoSummary { Overview = "ok" };
                context.StoredPdf = new StoredFile("file-id", "shop.pdf", "application/pdf", 1234);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStep(int order, SummaryStage stage, int start, List<string> log, bool reportHalfway) : ISummaryPipelineStep
    {
        public int Order => order;

        public SummaryStage Stage => stage;

        public int StartProgress => start;

        public async Task ExecuteAsync(SummaryPipelineContext context, CancellationToken cancellationToken)
        {
            log.Add(stage.ToString());
            if (reportHalfway)
            {
                await context.ReportProgress(0.5, cancellationToken);
            }
        }
    }
}
