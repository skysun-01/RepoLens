using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using RepoLens.Application.Common.Abstractions.Messaging;

namespace RepoLens.Infrastructure.Messaging.ServiceBus;

/// <summary>Publishes to Azure Service Bus. MessageId is the message's id, so queue duplicate detection drops re-sends.</summary>
internal sealed class ServiceBusMessagePublisher(ServiceBusClient client, MessageTopology topology) : IMessagePublisher, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new(StringComparer.Ordinal);

    public async Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        where TMessage : class, IIntegrationMessage
    {
        var sender = _senders.GetOrAdd(topology.EntityFor(typeof(TMessage)), client.CreateSender);

        var serviceBusMessage = new ServiceBusMessage(MessageSerializer.Serialize(message))
        {
            MessageId = message.MessageId.ToString("N"),
            ContentType = "application/json",
            Subject = typeof(TMessage).Name,
        };
        serviceBusMessage.ApplicationProperties[MessageSerializer.MessageTypeProperty] = typeof(TMessage).Name;

        await sender.SendMessageAsync(serviceBusMessage, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }
    }
}
