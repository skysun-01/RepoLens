using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RepoLens.Application.Summaries;

namespace RepoLens.Infrastructure.Messaging;

/// <summary>Which Service Bus entity each message type travels on, including a subscription for consumed topics.</summary>
public sealed class MessageTopology
{
    private readonly Dictionary<Type, (string Entity, string? Subscription)> _routes;

    public MessageTopology(IOptions<MessagingOptions> options)
    {
        var serviceBus = options.Value.ServiceBus;
        _routes = new Dictionary<Type, (string Entity, string? Subscription)>
        {
            [typeof(GenerateSummaryCommand)] = (serviceBus.SummaryRequestsTopic, serviceBus.SummaryRequestsSubscription),
            [typeof(SummaryCompletedEvent)] = (serviceBus.SummaryEventsTopic, null),
            [typeof(SummaryFailedEvent)] = (serviceBus.SummaryEventsTopic, null),
        };
    }

    public string EntityFor(Type messageType) =>
        DestinationFor(messageType).Entity;

    public string? SubscriptionFor(Type messageType) => DestinationFor(messageType).Subscription;

    private (string Entity, string? Subscription) DestinationFor(Type messageType) =>
        _routes.TryGetValue(messageType, out var destination)
            ? destination
            : throw new InvalidOperationException($"No queue or topic is configured for message type {messageType.Name}.");
}

/// <summary>JSON wire format shared by every transport.</summary>
internal static class MessageSerializer
{
    public const string MessageTypeProperty = "messageType";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static BinaryData Serialize<TMessage>(TMessage message) => BinaryData.FromObjectAsJson(message, Options);

    public static TMessage? Deserialize<TMessage>(BinaryData body) => body.ToObjectFromJson<TMessage>(Options);
}
