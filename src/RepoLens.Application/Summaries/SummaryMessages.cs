using System.Security.Cryptography;
using System.Text;
using RepoLens.Application.Common.Abstractions.Messaging;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries;

/// <summary>Command on the <c>summary-requests</c> queue: summarize the repository of this job.</summary>
public sealed record GenerateSummaryCommand(Guid JobId, Guid UserId, Guid RepoId, DateTimeOffset RequestedAt) : IIntegrationMessage
{
    /// <summary>The job id, so re-publishing the same job is dropped by broker duplicate detection.</summary>
    public Guid MessageId => JobId;

    public static GenerateSummaryCommand For(SummaryJob job) => new(job.Id, job.UserId, job.RepoId, job.CreatedAt);
}

/// <summary>Event on the <c>summary-events</c> topic: a summary PDF is ready.</summary>
public sealed record SummaryCompletedEvent(
    Guid JobId,
    Guid UserId,
    Guid RepoId,
    string RepoFullName,
    string CommitSha,
    DateTimeOffset OccurredAt) : IIntegrationMessage
{
    public Guid MessageId => DeterministicGuid.Create(JobId, "summary-completed");
}

/// <summary>Event on the <c>summary-events</c> topic: a summary job gave up.</summary>
public sealed record SummaryFailedEvent(
    Guid JobId,
    Guid UserId,
    Guid RepoId,
    string RepoFullName,
    string ErrorCode,
    string ErrorMessage,
    DateTimeOffset OccurredAt) : IIntegrationMessage
{
    public Guid MessageId => DeterministicGuid.Create(JobId, "summary-failed");
}

internal static class DeterministicGuid
{
    /// <summary>The same inputs always give the same id, so a retried publish is recognized as a duplicate.</summary>
    public static Guid Create(Guid seed, string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed:N}:{name}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
