namespace RepoLens.Application.Common.Abstractions.Messaging;

/// <summary>A command or event sent between services through the message broker.</summary>
public interface IIntegrationMessage
{
    /// <summary>Unique per logical message; the broker uses it to drop duplicates.</summary>
    Guid MessageId { get; }
}

/// <summary>
/// Sends messages to the broker. Which queue or topic a message type goes to is configured in
/// Infrastructure, so callers never name an entity.
/// </summary>
public interface IMessagePublisher
{
    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        where TMessage : class, IIntegrationMessage;
}

/// <summary>
/// Handles one message type. Returning normally completes the message; throwing makes the broker
/// redeliver it until <see cref="MessageContext.MaxDeliveryCount"/> is reached, then dead-letter it.
/// </summary>
public interface IMessageHandler<in TMessage>
    where TMessage : class, IIntegrationMessage
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken);
}

public sealed record MessageContext(string MessageId, int DeliveryCount, int MaxDeliveryCount)
{
    public bool IsLastAttempt => DeliveryCount >= MaxDeliveryCount;
}
