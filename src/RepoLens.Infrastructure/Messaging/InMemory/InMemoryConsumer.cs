using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoLens.Application.Common.Abstractions.Messaging;

namespace RepoLens.Infrastructure.Messaging.InMemory;

/// <summary>
/// In-process counterpart of the Service Bus consumer, with the same retry, backoff and
/// dead-letter behaviour. Used for local development and tests.
/// </summary>
internal sealed partial class InMemoryConsumer<TMessage>(
    InMemoryMessageBus bus,
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingOptions> options,
    ILogger<InMemoryConsumer<TMessage>> logger) : BackgroundService
    where TMessage : class, IIntegrationMessage
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = bus.ChannelFor(typeof(TMessage));
        var workers = Enumerable.Range(0, options.Value.MaxConcurrentMessages)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await foreach (var envelope in channel.Reader.ReadAllAsync(stoppingToken))
                    {
                        await ProcessAsync(envelope, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Shutting down.
                }
            }, CancellationToken.None));

        return Task.WhenAll(workers);
    }

    private async Task ProcessAsync(InMemoryEnvelope envelope, CancellationToken stoppingToken)
    {
        TMessage? message;
        try
        {
            message = MessageSerializer.Deserialize<TMessage>(envelope.Body);
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message is null)
        {
            bus.DeadLetter(envelope);
            LogDeadLettered(envelope.MessageId, envelope.DeliveryCount);
            return;
        }

        var settings = options.Value;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler<TMessage>>();
            await handler.HandleAsync(message, new MessageContext(envelope.MessageId, envelope.DeliveryCount, settings.MaxDeliveryCount), stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            if (envelope.DeliveryCount >= settings.MaxDeliveryCount)
            {
                bus.DeadLetter(envelope);
                LogDeadLettered(envelope.MessageId, envelope.DeliveryCount);
                return;
            }

            var delay = settings.RetryDelay(envelope.DeliveryCount);
            LogRetrying(ex, envelope.MessageId, envelope.DeliveryCount, delay);
            await Task.Delay(delay, stoppingToken);
            await bus.ChannelFor(typeof(TMessage)).Writer.WriteAsync(envelope with { DeliveryCount = envelope.DeliveryCount + 1 }, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message {MessageId} failed on delivery {DeliveryCount}; redelivering after {Delay}")]
    private partial void LogRetrying(Exception exception, string messageId, int deliveryCount, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Message {MessageId} dead-lettered after {DeliveryCount} deliveries")]
    private partial void LogDeadLettered(string messageId, int deliveryCount);
}
