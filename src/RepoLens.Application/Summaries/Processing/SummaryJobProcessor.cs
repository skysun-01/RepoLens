using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Application.Common.Abstractions.Messaging;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Errors;
using RepoLens.Application.Users;
using RepoLens.Domain.Common;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries.Processing;

/// <summary>
/// Consumes <see cref="GenerateSummaryCommand"/>. Safe to receive the same command more than once:
/// a job is claimed with an optimistic-concurrency check, and finished jobs are skipped.
/// <list type="bullet">
/// <item>Permanent errors (bad token, repository gone, invalid content) fail the job and complete the message.</item>
/// <item>Transient errors put the job back in the queue and rethrow, so the broker redelivers.</item>
/// <item>On the last delivery attempt the job is failed and the message is dead-lettered.</item>
/// </list>
/// </summary>
public sealed partial class SummaryJobProcessor(
    ISummaryJobRepository jobs,
    IRepoRepository repos,
    GitHubTokenAccessor tokenAccessor,
    SummaryPipeline pipeline,
    IMessagePublisher publisher,
    IOptions<SummaryProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<SummaryJobProcessor> logger) : IMessageHandler<GenerateSummaryCommand>
{
    public async Task HandleAsync(GenerateSummaryCommand message, MessageContext context, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(message.JobId, cancellationToken);
        if (job is null)
        {
            LogJobMissing(message.JobId);
            return;
        }

        if (!await TryClaimAsync(job, cancellationToken))
        {
            return;
        }

        LogStarted(job.Id, job.RepoFullName, job.Attempts, context.DeliveryCount);

        var repo = await repos.GetAsync(job.UserId, job.RepoId, cancellationToken);
        if (repo is null)
        {
            await FailAsync(job, new JobError("repo_not_found", "The repository is no longer in your list. Sync your repositories and try again."));
            return;
        }

        await using var heartbeat = JobHeartbeat.Start(job.Id, jobs, options.Value.HeartbeatInterval, timeProvider, logger, cancellationToken);

        try
        {
            var (_, accessToken) = await tokenAccessor.GetAsync(job.UserId, cancellationToken);
            var pipelineContext = new SummaryPipelineContext(job, repo, accessToken);
            await pipeline.RunAsync(pipelineContext, cancellationToken);

            job.Complete(pipelineContext.RequireStoredPdf(), pipelineContext.RequireSummary(), timeProvider.GetUtcNow());
            await jobs.UpdateAsync(job, CancellationToken.None);
            LogCompleted(job.Id, job.RepoFullName);

            await TryPublishAsync(new SummaryCompletedEvent(job.Id, job.UserId, job.RepoId, job.RepoFullName, job.CommitSha!, job.CompletedAt!.Value));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is shutting down. Hand the job back so the redelivered message can resume it.
            await TryReturnToQueueAsync(job, new JobError("worker_shutdown", "The worker restarted; the job will resume."));
            throw;
        }
        catch (ConcurrencyException)
        {
            // The stale-job sweeper re-queued this job while we were working; the new owner finishes it.
            LogLostOwnership(job.Id);
        }
        catch (Exception ex) when (IsPermanent(ex))
        {
            LogPermanentFailure(ex, job.Id);
            await FailAsync(job, ToJobError(ex));
        }
        catch (Exception ex)
        {
            var error = ToJobError(ex);
            if (context.IsLastAttempt)
            {
                LogGaveUp(ex, job.Id, context.DeliveryCount);
                await FailAsync(job, error);
            }
            else
            {
                LogWillRetry(ex, job.Id, context.DeliveryCount, context.MaxDeliveryCount);
                await TryReturnToQueueAsync(job, error);
            }

            throw;
        }
    }

    private async Task<bool> TryClaimAsync(SummaryJob job, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!job.CanBeClaimed(now, options.Value.StaleAfter))
        {
            LogSkipped(job.Id, job.Status);
            return false;
        }

        job.Start(now, options.Value.StaleAfter);
        try
        {
            await jobs.UpdateAsync(job, cancellationToken);
            return true;
        }
        catch (ConcurrencyException)
        {
            LogSkipped(job.Id, SummaryStatus.Processing);
            return false;
        }
    }

    private async Task FailAsync(SummaryJob job, JobError error)
    {
        try
        {
            job.Fail(error, timeProvider.GetUtcNow());
            await jobs.UpdateAsync(job, CancellationToken.None);
        }
        catch (ConcurrencyException)
        {
            LogLostOwnership(job.Id);
            return;
        }

        await TryPublishAsync(new SummaryFailedEvent(job.Id, job.UserId, job.RepoId, job.RepoFullName, error.Code, error.Message, timeProvider.GetUtcNow()));
    }

    private async Task TryReturnToQueueAsync(SummaryJob job, JobError error)
    {
        try
        {
            job.ReturnToQueue(error, timeProvider.GetUtcNow());
            await jobs.UpdateAsync(job, CancellationToken.None);
        }
        catch (Exception ex) when (ex is ConcurrencyException or DomainException)
        {
            LogLostOwnership(job.Id);
        }
    }

    /// <summary>Completion events are informational; a failure to publish must not fail the job.</summary>
    private async Task TryPublishAsync<TEvent>(TEvent @event)
        where TEvent : class, IIntegrationMessage
    {
        try
        {
            await publisher.PublishAsync(@event, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogEventPublishFailed(ex, typeof(TEvent).Name);
        }
    }

    private static bool IsPermanent(Exception exception) => exception is
        PermanentJobException
        or GitHubAuthorizationException
        or GitHubNotFoundException
        or RepositoryEmptyException
        or RepositoryTooLargeException
        or DomainException
        or AppException { Kind: ErrorKind.Validation or ErrorKind.NotFound or ErrorKind.Unauthorized };

    /// <summary>Known errors keep their message; unexpected ones get a generic message so internals do not leak to users.</summary>
    private static JobError ToJobError(Exception exception) => exception switch
    {
        AppException app => new JobError(app.Code, app.Message),
        DomainException => new JobError("invalid_state", "The job could not continue because of an invalid state."),
        _ => new JobError("processing_error", "An unexpected error occurred while generating the summary."),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Summary job {JobId} no longer exists; message dropped")]
    private partial void LogJobMissing(Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Summary job {JobId} is {Status}; skipping duplicate delivery")]
    private partial void LogSkipped(Guid jobId, SummaryStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing summary job {JobId} for {Repo} (attempt {Attempt}, delivery {DeliveryCount})")]
    private partial void LogStarted(Guid jobId, string repo, int attempt, int deliveryCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Summary job {JobId} for {Repo} completed")]
    private partial void LogCompleted(Guid jobId, string repo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Summary job {JobId} was taken over by another worker")]
    private partial void LogLostOwnership(Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Summary job {JobId} failed permanently")]
    private partial void LogPermanentFailure(Exception exception, Guid jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Summary job {JobId} failed on its last delivery ({DeliveryCount}); dead-lettering")]
    private partial void LogGaveUp(Exception exception, Guid jobId, int deliveryCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Summary job {JobId} failed on delivery {DeliveryCount} of {MaxDeliveryCount}; will retry")]
    private partial void LogWillRetry(Exception exception, Guid jobId, int deliveryCount, int maxDeliveryCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish {EventType}")]
    private partial void LogEventPublishFailed(Exception exception, string eventType);
}
