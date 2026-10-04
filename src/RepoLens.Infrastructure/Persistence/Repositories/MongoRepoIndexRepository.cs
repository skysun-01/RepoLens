using MongoDB.Driver;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Domain.Repos;

namespace RepoLens.Infrastructure.Persistence.Repositories;

internal sealed class MongoRepoIndexRepository(MongoContext context) : IRepoIndexRepository
{
    public async Task<RepoIndex?> GetAsync(Guid repoId, CancellationToken cancellationToken) =>
        await context.RepoIndexes.Find(i => i.RepoId == repoId).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, RepoIndex>> GetManyAsync(IReadOnlyCollection<Guid> repoIds, CancellationToken cancellationToken)
    {
        if (repoIds.Count == 0)
        {
            return new Dictionary<Guid, RepoIndex>();
        }

        var indexes = await context.RepoIndexes.Find(Builders<RepoIndex>.Filter.In(i => i.RepoId, repoIds)).ToListAsync(cancellationToken);
        return indexes.ToDictionary(i => i.RepoId);
    }

    public Task UpsertAsync(RepoIndex index, CancellationToken cancellationToken) =>
        context.RepoIndexes.ReplaceOneAsync(i => i.RepoId == index.RepoId, index, new ReplaceOptions { IsUpsert = true }, cancellationToken);

    public Task DeleteManyAsync(IReadOnlyCollection<Guid> repoIds, CancellationToken cancellationToken) =>
        repoIds.Count == 0
            ? Task.CompletedTask
            : context.RepoIndexes.DeleteManyAsync(Builders<RepoIndex>.Filter.In(i => i.RepoId, repoIds), cancellationToken);
}
