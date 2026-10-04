using Microsoft.Extensions.Logging;
using RepoLens.Application.Common.Abstractions.Persistence;

namespace RepoLens.Application.Summaries.Processing;

/// <summary>
/// Periodically stamps the job while a worker holds it, so the outbox dispatcher can tell a slow job
/// from one whose worker crashed.
/// </summary>
internal sealed partial class JobHeartbeat : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task _loop;
    private readonly ILogger _logger;

    private JobHeartbeat(Guid jobId, ISummaryJobRepository jobs, TimeSpan interval, TimeProvider timeProvider, ILogger logger, CancellationToken cancellationToken)
    {
        _logger = logger;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunAsync(jobId, jobs, interval, timeProvider, _stop.Token);
    }

    public static JobHeartbeat Start(Guid jobId, ISummaryJobRepository jobs, TimeSpan interval, TimeProvider timeProvider, ILogger logger, CancellationToken cancellationToken) =>
        new(jobId, jobs, interval, timeProvider, logger, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }

        _stop.Dispose();
    }

    private async Task RunAsync(Guid jobId, ISummaryJobRepository jobs, TimeSpan interval, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval, timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await jobs.TouchHeartbeatAsync(jobId, timeProvider.GetUtcNow(), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogHeartbeatFailed(ex, jobId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Heartbeat for job {JobId} failed")]
    private partial void LogHeartbeatFailed(Exception exception, Guid jobId);
}
