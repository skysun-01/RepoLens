using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Common.Abstractions.Persistence;

public interface ISummaryJobRepository
{
    Task<SummaryJob?> GetAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Returns the job only if it belongs to <paramref name="userId"/>.</summary>
    Task<SummaryJob?> GetForUserAsync(Guid userId, Guid jobId, CancellationToken cancellationToken);

    Task<SummaryJob?> GetActiveForRepoAsync(Guid repoId, CancellationToken cancellationToken);

    Task<SummaryJob?> GetLatestCompletedAsync(Guid repoId, string? commitSha, CancellationToken cancellationToken);

    Task<IReadOnlyList<SummaryJob>> ListForRepoAsync(Guid userId, Guid repoId, int limit, CancellationToken cancellationToken);

    /// <summary>Most recent job per repository, optionally only completed ones. Used to fill the repo table.</summary>
    Task<IReadOnlyDictionary<Guid, SummaryJob>> GetLatestPerRepoAsync(
        IReadOnlyCollection<Guid> repoIds,
        bool completedOnly,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a new job. Returns false when the repository already has an active job; a unique
    /// index enforces this, so two simultaneous clicks cannot both create one.
    /// </summary>
    Task<bool> TryAddAsync(SummaryJob job, CancellationToken cancellationToken);

    /// <summary>Saves a state transition; throws <see cref="Errors.ConcurrencyException"/> if someone else saved first.</summary>
    Task UpdateAsync(SummaryJob job, CancellationToken cancellationToken);

    /// <summary>Sets the outbox marker only. Does not conflict with a worker that is already processing.</summary>
    Task MarkPublishedAsync(Guid jobId, DateTimeOffset publishedAt, CancellationToken cancellationToken);

    /// <summary>Records stage, progress and heartbeat only, without a version check.</summary>
    Task SaveProgressAsync(SummaryJob job, CancellationToken cancellationToken);

    Task TouchHeartbeatAsync(Guid jobId, DateTimeOffset heartbeatAt, CancellationToken cancellationToken);

    /// <summary>Queued jobs whose command has not reached the broker yet (the outbox).</summary>
    Task<IReadOnlyList<SummaryJob>> GetUnpublishedAsync(DateTimeOffset createdBefore, int limit, CancellationToken cancellationToken);

    /// <summary>Processing jobs whose worker stopped sending heartbeats.</summary>
    Task<IReadOnlyList<SummaryJob>> GetStaleAsync(DateTimeOffset heartbeatBefore, int limit, CancellationToken cancellationToken);
}
