using RepoLens.Domain.Common;
using RepoLens.Domain.Summaries;

namespace RepoLens.UnitTests.Domain;

public sealed class SummaryJobTests
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    [Fact]
    public void New_job_is_queued_active_and_unpublished()
    {
        var job = TestData.Job();

        Assert.Equal(SummaryStatus.Queued, job.Status);
        Assert.True(job.IsActive);
        Assert.Null(job.PublishedAt);
        Assert.Equal(0, job.Attempts);
    }

    [Fact]
    public void Start_moves_to_processing_and_counts_the_attempt()
    {
        var job = TestData.Job();

        job.Start(TestData.Now, StaleAfter);

        Assert.Equal(SummaryStatus.Processing, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(TestData.Now, job.HeartbeatAt);
        Assert.NotNull(job.PublishedAt);
    }

    [Fact]
    public void A_processing_job_with_a_fresh_heartbeat_cannot_be_claimed_twice()
    {
        var job = TestData.Job();
        job.Start(TestData.Now, StaleAfter);

        Assert.False(job.CanBeClaimed(TestData.Now.AddMinutes(1), StaleAfter));
        Assert.Throws<DomainException>(() => job.Start(TestData.Now.AddMinutes(1), StaleAfter));
    }

    [Fact]
    public void A_processing_job_whose_heartbeat_stopped_can_be_claimed_again()
    {
        var job = TestData.Job();
        job.Start(TestData.Now, StaleAfter);

        var later = TestData.Now.AddMinutes(20);

        Assert.True(job.CanBeClaimed(later, StaleAfter));
        job.Start(later, StaleAfter);
        Assert.Equal(2, job.Attempts);
    }

    [Fact]
    public void Complete_stores_the_pdf_and_releases_the_active_slot()
    {
        var job = TestData.Job();
        job.Start(TestData.Now, StaleAfter);

        job.Complete(new StoredFile("id", "shop.pdf", "application/pdf", 10), new RepoSummary { Overview = "x" }, TestData.Now.AddMinutes(2));

        Assert.Equal(SummaryStatus.Completed, job.Status);
        Assert.Equal(100, job.Progress);
        Assert.False(job.IsActive);
        Assert.NotNull(job.Pdf);
    }

    [Fact]
    public void Complete_requires_a_pinned_commit()
    {
        var job = TestData.Job(sha: null);
        job.Start(TestData.Now, StaleAfter);

        Assert.Throws<DomainException>(() =>
            job.Complete(new StoredFile("id", "shop.pdf", "application/pdf", 10), new RepoSummary(), TestData.Now));
    }

    [Fact]
    public void Progress_never_goes_backwards_and_stays_below_100_while_processing()
    {
        var job = TestData.Job();
        job.Start(TestData.Now, StaleAfter);

        job.ReportProgress(SummaryStage.GeneratingSummary, 60, TestData.Now);
        job.ReportProgress(SummaryStage.RenderingPdf, 40, TestData.Now);
        Assert.Equal(60, job.Progress);

        job.ReportProgress(SummaryStage.StoringPdf, 150, TestData.Now);
        Assert.Equal(99, job.Progress);
    }

    [Fact]
    public void ReturnToQueue_keeps_the_error_and_the_outbox_marker()
    {
        var job = TestData.Job();
        job.Start(TestData.Now, StaleAfter);

        job.ReturnToQueue(new JobError("github_unavailable", "later"), TestData.Now);

        Assert.Equal(SummaryStatus.Queued, job.Status);
        Assert.True(job.IsActive);
        Assert.NotNull(job.PublishedAt);
        Assert.Equal("github_unavailable", job.Error?.Code);
    }

    [Fact]
    public void RequeueStale_clears_the_outbox_marker_so_the_command_is_published_again()
    {
        var job = TestData.Job();
        job.Start(TestData.Now, StaleAfter);

        job.RequeueStale(TestData.Now.AddMinutes(30), StaleAfter);

        Assert.Equal(SummaryStatus.Queued, job.Status);
        Assert.Null(job.PublishedAt);
        Assert.Equal("worker_timeout", job.Error?.Code);
    }

    [Fact]
    public void A_finished_job_cannot_fail_again()
    {
        var job = TestData.Job();
        job.Fail(new JobError("x", "y"), TestData.Now);

        Assert.False(job.IsActive);
        Assert.Throws<DomainException>(() => job.Fail(new JobError("x", "y"), TestData.Now));
    }

    [Fact]
    public void PinCommit_refuses_to_change_an_already_pinned_commit()
    {
        var job = TestData.Job();
        job.Start(TestData.Now, StaleAfter);

        job.PinCommit(TestData.Sha.ToUpperInvariant());
        Assert.Throws<DomainException>(() => job.PinCommit("ffffffffffffffffffffffffffffffffffffffff"));
    }
}
