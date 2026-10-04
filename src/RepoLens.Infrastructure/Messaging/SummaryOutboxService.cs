using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoLens.Application.Summaries;
using RepoLens.Application.Summaries.Outbox;

namespace RepoLens.Infrastructure.Messaging;

/// <summary>Runs the outbox dispatcher on a timer: publishes unpublished jobs and re-queues stale ones.</summary>
internal sealed partial class SummaryOutboxService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<SummaryOutboxService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.PollInterval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<SummaryOutboxDispatcher>();
                await dispatcher.RequeueStaleAsync(stoppingToken);
                await dispatcher.PublishPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPassFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox pass failed; retrying on the next tick")]
    private partial void LogPassFailed(Exception exception);
}

public static class SummaryOutboxServiceCollectionExtensions
{
    /// <summary>Hosts the outbox dispatcher. Requires <c>AddApplication()</c>.</summary>
    public static IServiceCollection AddSummaryOutbox(this IServiceCollection services)
    {
        services.AddHostedService<SummaryOutboxService>();
        return services;
    }
}
