using System.ComponentModel.DataAnnotations;

namespace RepoLens.Infrastructure.Messaging;

public enum MessagingProvider
{
    /// <summary>In-process channels. Summaries are processed inside the API. For local development and tests.</summary>
    InMemory,

    /// <summary>Azure Service Bus. The Worker app processes summaries.</summary>
    ServiceBus,
}

public sealed class MessagingOptions : IValidatableObject
{
    public const string SectionName = "Messaging";

    public MessagingProvider Provider { get; set; } = MessagingProvider.InMemory;

    /// <summary>Attempts per message before it is dead-lettered. Must equal the queue or subscription MaxDeliveryCount in Azure.</summary>
    [Range(1, 100)]
    public int MaxDeliveryCount { get; set; } = 5;

    /// <summary>Messages processed at the same time by one worker instance.</summary>
    [Range(1, 64)]
    public int MaxConcurrentMessages { get; set; } = 2;

    /// <summary>First retry delay; doubles on every further attempt up to <see cref="RetryMaxDelay"/>.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:10:00")]
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(2);

    public ServiceBusSettings ServiceBus { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider == MessagingProvider.ServiceBus
            && string.IsNullOrWhiteSpace(ServiceBus.FullyQualifiedNamespace)
            && string.IsNullOrWhiteSpace(ServiceBus.ConnectionString))
        {
            yield return new ValidationResult(
                "Messaging:ServiceBus:FullyQualifiedNamespace (managed identity) or Messaging:ServiceBus:ConnectionString is required when Provider is ServiceBus.",
                [nameof(ServiceBus)]);
        }
    }

    public TimeSpan RetryDelay(int deliveryCount)
    {
        var exponent = Math.Clamp(deliveryCount - 1, 0, 16);
        var delay = TimeSpan.FromTicks(RetryBaseDelay.Ticks * (1L << exponent));
        return delay > RetryMaxDelay ? RetryMaxDelay : delay;
    }
}

public sealed class ServiceBusSettings
{
    /// <summary>For example <c>repolens-prod.servicebus.windows.net</c>; authenticates with managed identity.</summary>
    public string? FullyQualifiedNamespace { get; set; }

    /// <summary>Alternative to managed identity, for local runs against a real namespace.</summary>
    public string? ConnectionString { get; set; }

    [Required]
    public string SummaryRequestsTopic { get; set; } = "summary-requests-topic";

    [Required]
    public string SummaryRequestsSubscription { get; set; } = "summary-worker";

    [Required]
    public string SummaryEventsTopic { get; set; } = "summary-events";

    /// <summary>How long the processor keeps renewing a message lock while a summary is generated.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan MaxAutoLockRenewalDuration { get; set; } = TimeSpan.FromMinutes(30);
}
