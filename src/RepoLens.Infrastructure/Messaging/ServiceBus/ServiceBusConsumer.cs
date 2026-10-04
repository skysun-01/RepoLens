using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoLens.Application.Common.Abstractions.Messaging;

namespace RepoLens.Infrastructure.Messaging.ServiceBus;

/// <summary>
/// Receives one message type from Service Bus with PeekLock and hands each message to its
/// <see cref="IMessageHandler{TMessage}"/> in a fresh DI scope. Success completes the message.
/// Failure waits an exponential backoff (the lock keeps renewing), then abandons it so Service Bus
/// redelivers; after MaxDeliveryCount the broker moves it to the dead-letter queue.
/// </summary>
internal sealed partial class ServiceBusConsumer<TMessage>(
    ServiceBusClient client,
    MessageTopology topology,
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingOptions> options,
    ILogger<ServiceBusConsumer<TMessage>> logger) : BackgroundService
    where TMessage : class, IIntegrationMessage
{
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var entity = topology.EntityFor(typeof(TMessage));
        var subscription = topology.SubscriptionFor(typeof(TMessage));

        var processorOptions = new ServiceBusProcessorOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            AutoCompleteMessages = false,
            MaxConcurrentCalls = settings.MaxConcurrentMessages,
            MaxAutoLockRenewalDuration = settings.ServiceBus.MaxAutoLockRenewalDuration,
            PrefetchCount = 0,
        };
        _processor = subscription is null
            ? client.CreateProcessor(entity, processorOptions)
            : client.CreateProcessor(entity, subscription, processorOptions);
        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
        LogListening(typeof(TMessage).Name, entity, settings.MaxConcurrentMessages);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var received = args.Message;
        TMessage? message;
        try
        {
            message = MessageSerializer.Deserialize<TMessage>(received.Body);
        }
        catch (JsonException ex)
        {
            message = null;
            LogInvalidPayload(ex, received.MessageId);
        }

        if (message is null)
        {
            // A message that cannot be read will never succeed; dead-letter it now instead of retrying.
            await args.DeadLetterMessageAsync(received, "invalid_payload", $"Body is not a valid {typeof(TMessage).Name}.", args.CancellationToken);
            return;
        }

        var settings = options.Value;
        var context = new MessageContext(received.MessageId, received.DeliveryCount, settings.MaxDeliveryCount);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler<TMessage>>();
            await handler.HandleAsync(message, context, args.CancellationToken);
            await args.CompleteMessageAsync(received, args.CancellationToken);
        }
        catch (Exception ex) when (!args.CancellationToken.IsCancellationRequested)
        {
            var delay = settings.RetryDelay(received.DeliveryCount);
            LogHandlerFailed(ex, received.MessageId, received.DeliveryCount, settings.MaxDeliveryCount, delay);

            try
            {
                await Task.Delay(delay, args.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Shutting down; abandon right away.
            }

            await args.AbandonMessageAsync(received, cancellationToken: CancellationToken.None);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        LogProcessorError(args.Exception, args.ErrorSource.ToString(), args.EntityPath);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening for {MessageType} on Service Bus entity {Entity} ({Concurrency} at a time)")]
    private partial void LogListening(string messageType, string entity, int concurrency);

    [LoggerMessage(Level = LogLevel.Error, Message = "Message {MessageId} has an invalid payload; dead-lettering")]
    private partial void LogInvalidPayload(Exception exception, string messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handling message {MessageId} failed on delivery {DeliveryCount}/{MaxDeliveryCount}; abandoning after {Delay}")]
    private partial void LogHandlerFailed(Exception exception, string messageId, int deliveryCount, int maxDeliveryCount, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Service Bus processor error ({ErrorSource}) on {EntityPath}")]
    private partial void LogProcessorError(Exception exception, string errorSource, string entityPath);
}
