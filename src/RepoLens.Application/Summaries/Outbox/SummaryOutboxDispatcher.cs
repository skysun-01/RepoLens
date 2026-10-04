using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoLens.Application.Common.Abstractions.Messaging;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Errors;

namespace RepoLens.Application.Summaries.Outbox;

/// <summary>
/// The reliability loop behind the event-driven flow. It publishes any queued job whose command never
/// reached the broker (the outbox), and re-queues jobs whose worker died mid-way.
/// </summary>
public sealed partial class SummaryOutboxDispatcher(
    ISummaryJobRepository jobs,
    IMessagePublisher publisher,
    IOptions<OutboxOptions> outboxOptions,
    IOptions<SummaryProcessingOptions> processingOptions,
    TimeProvider timeProvider,
    ILogger<SummaryOutboxDispatcher> logger)
{
    /// <returns>How many commands were published.</returns>
    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        var options = outboxOptions.Value;
        var pending = await jobs.GetUnpublishedAsync(timeProvider.GetUtcNow() - options.PublishGracePeriod, options.BatchSize, cancellationToken);
        var published = 0;

        foreach (var job in pending)
        {
            try
            {
                await publisher.PublishAsync(GenerateSummaryCommand.For(job), cancellationToken);
                await jobs.MarkPublishedAsync(job.Id, timeProvider.GetUtcNow(), cancellationToken);
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The broker is probably unreachable; stop this pass and try again on the next one.
                LogPublishFailed(ex, job.Id);
                break;
            }
        }

        if (published > 0)
        {
            LogPublished(published);
        }

        return published;
    }

    /// <returns>How many stale jobs were queued again.</returns>
    public async Task<int> RequeueStaleAsync(CancellationToken cancellationToken)
    {
        var staleAfter = processingOptions.Value.StaleAfter;
        var now = timeProvider.GetUtcNow();
        var stale = await jobs.GetStaleAsync(now - staleAfter, outboxOptions.Value.BatchSize, cancellationToken);
        var requeued = 0;

        foreach (var job in stale)
        {
            if (!job.IsHeartbeatStale(now, staleAfter))
            {
                continue;
            }

            job.RequeueStale(now, staleAfter);
            try
            {
                await jobs.UpdateAsync(job, cancellationToken);
                requeued++;
                LogRequeued(job.Id, job.Attempts);
            }
            catch (ConcurrencyException)
            {
                // The worker woke up and saved progress first; leave it alone.
            }
        }

        return requeued;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox published {Count} pending summary command(s)")]
    private partial void LogPublished(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox could not publish job {JobId}; will retry")]
    private partial void LogPublishFailed(Exception exception, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} stopped sending heartbeats after {Attempts} attempt(s); queued again")]
    private partial void LogRequeued(Guid jobId, int attempts);
}
