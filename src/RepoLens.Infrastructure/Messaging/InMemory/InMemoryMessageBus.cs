using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RepoLens.Application.Common.Abstractions.Messaging;

namespace RepoLens.Infrastructure.Messaging.InMemory;

internal sealed record InMemoryEnvelope(string MessageId, BinaryData Body, int DeliveryCount);

/// <summary>Marks a message type as consumed in this process, so the bus keeps a channel for it.</summary>
internal sealed record InMemoryConsumerRegistration(Type MessageType);

/// <summary>
/// One channel per consumed message type. Messages go through the same JSON serialization as on
/// Service Bus, so wire-format problems show up locally too. Messages with no consumer in this
/// process are dropped, like a topic with no subscriptions.
/// </summary>
internal sealed class InMemoryMessageBus(IEnumerable<InMemoryConsumerRegistration> registrations)
{
    private readonly HashSet<Type> _consumed = registrations.Select(r => r.MessageType).ToHashSet();
    private readonly ConcurrentDictionary<Type, Channel<InMemoryEnvelope>> _channels = new();
    private readonly ConcurrentQueue<InMemoryEnvelope> _deadLetters = new();

    public IReadOnlyCollection<InMemoryEnvelope> DeadLetters => _deadLetters;

    public bool IsConsumed(Type messageType) => _consumed.Contains(messageType);

    public Channel<InMemoryEnvelope> ChannelFor(Type messageType) =>
        _channels.GetOrAdd(messageType, _ => Channel.CreateUnbounded<InMemoryEnvelope>(new UnboundedChannelOptions { SingleReader = false }));

    public void DeadLetter(InMemoryEnvelope envelope) => _deadLetters.Enqueue(envelope);
}

internal sealed partial class InMemoryMessagePublisher(InMemoryMessageBus bus, ILogger<InMemoryMessagePublisher> logger) : IMessagePublisher
{
    public async Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        where TMessage : class, IIntegrationMessage
    {
        if (!bus.IsConsumed(typeof(TMessage)))
        {
            LogNoConsumer(typeof(TMessage).Name, message.MessageId);
            return;
        }

        var envelope = new InMemoryEnvelope(message.MessageId.ToString("N"), MessageSerializer.Serialize(message), DeliveryCount: 1);
        await bus.ChannelFor(typeof(TMessage)).Writer.WriteAsync(envelope, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "No in-process consumer for {MessageType}; message {MessageId} dropped")]
    private partial void LogNoConsumer(string messageType, Guid messageId);
}
