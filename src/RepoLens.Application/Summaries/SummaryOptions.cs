using System.ComponentModel.DataAnnotations;

namespace RepoLens.Application.Summaries;

/// <summary>How workers claim and keep jobs alive.</summary>
public sealed class SummaryProcessingOptions
{
    public const string SectionName = "Summaries:Processing";

    /// <summary>
    /// A processing job whose heartbeat is older than this is considered abandoned and queued again.
    /// Keep it longer than the Service Bus duplicate-detection window so the re-published command is not dropped.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(15);

    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>The transactional-outbox dispatcher that guarantees every queued job reaches the broker.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Summaries:Outbox";

    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gives the API's immediate publish time to finish before the dispatcher steps in.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:10:00")]
    public TimeSpan PublishGracePeriod { get; set; } = TimeSpan.FromSeconds(15);

    [Range(1, 1_000)]
    public int BatchSize { get; set; } = 50;
}

/// <summary>Budgets for the map → reduce summary generation.</summary>
public sealed class SummaryGenerationOptions
{
    public const string SectionName = "Summaries:Generation";

    /// <summary>Repositories whose readable content fits in this many characters skip the map phase.</summary>
    [Range(1_000, 1_000_000)]
    public int SinglePassMaxChars { get; set; } = 60_000;

    [Range(1, 64)]
    public int MaxModules { get; set; } = 16;

    /// <summary>File content sent per module in the map phase (about 4 characters per token).</summary>
    [Range(1_000, 1_000_000)]
    public int MaxCharsPerModule { get; set; } = 40_000;

    [Range(1_000, 200_000)]
    public int MaxReadmeChars { get; set; } = 12_000;

    /// <summary>Manifests, configuration, CI and entry-point files sent to the reduce phase.</summary>
    [Range(1_000, 500_000)]
    public int MaxKeyFileChars { get; set; } = 30_000;

    [Range(10, 10_000)]
    public int MaxTreePaths { get; set; } = 400;

    [Range(1, 32)]
    public int MapConcurrency { get; set; } = 4;

    [Range(256, 128_000)]
    public int MapMaxOutputTokens { get; set; } = 4_000;

    [Range(256, 128_000)]
    public int ReduceMaxOutputTokens { get; set; } = 16_000;

    /// <summary>Attempts to get valid structured output from the model before the job fails.</summary>
    [Range(1, 5)]
    public int MaxModelAttempts { get; set; } = 2;
}
