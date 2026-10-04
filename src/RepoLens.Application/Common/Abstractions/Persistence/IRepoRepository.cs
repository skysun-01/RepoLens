using RepoLens.Application.Common.Models;
using RepoLens.Domain.Repos;

namespace RepoLens.Application.Common.Abstractions.Persistence;

public interface IRepoRepository
{
    /// <summary>Returns the repository only if it belongs to <paramref name="userId"/>.</summary>
    Task<Repo?> GetAsync(Guid userId, Guid repoId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Repo>> ListAllAsync(Guid userId, CancellationToken cancellationToken);

    Task<PagedResult<Repo>> SearchAsync(Guid userId, RepoSearch search, CancellationToken cancellationToken);

    /// <summary>Applies the result of a GitHub sync in one round trip.</summary>
    Task SaveSyncAsync(
        Guid userId,
        IReadOnlyCollection<Repo> added,
        IReadOnlyCollection<Repo> updated,
        IReadOnlyCollection<Guid> removedIds,
        CancellationToken cancellationToken);
}

public enum RepoSort
{
    RecentlyPushed,
    Name,
    Stars,
}

public sealed record RepoSearch(string? Text, RepoSort Sort, int Page, int PageSize);
