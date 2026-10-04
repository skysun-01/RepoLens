using RepoLens.Domain.Repos;

namespace RepoLens.Application.Common.Abstractions.Persistence;

public interface IRepoIndexRepository
{
    Task<RepoIndex?> GetAsync(Guid repoId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, RepoIndex>> GetManyAsync(IReadOnlyCollection<Guid> repoIds, CancellationToken cancellationToken);

    Task UpsertAsync(RepoIndex index, CancellationToken cancellationToken);

    Task DeleteManyAsync(IReadOnlyCollection<Guid> repoIds, CancellationToken cancellationToken);
}
