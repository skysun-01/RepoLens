using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Models;
using RepoLens.Domain.Repos;

namespace RepoLens.Infrastructure.Persistence.Repositories;

internal sealed class MongoRepoRepository(MongoContext context) : IRepoRepository
{
    private static FilterDefinitionBuilder<Repo> Filter => Builders<Repo>.Filter;

    public async Task<Repo?> GetAsync(Guid userId, Guid repoId, CancellationToken cancellationToken) =>
        await context.Repos.Find(r => r.Id == repoId && r.UserId == userId).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Repo>> ListAllAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.Repos.Find(r => r.UserId == userId).ToListAsync(cancellationToken);

    public async Task<PagedResult<Repo>> SearchAsync(Guid userId, RepoSearch search, CancellationToken cancellationToken)
    {
        var filter = Filter.Eq(r => r.UserId, userId);
        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(search.Text), "i");
            filter &= Filter.Or(
                Filter.Regex(r => r.FullName, pattern),
                Filter.Regex(r => r.Description, pattern),
                Filter.Regex(r => r.Language, pattern));
        }

        var sort = search.Sort switch
        {
            RepoSort.Name => Builders<Repo>.Sort.Ascending(r => r.Name),
            RepoSort.Stars => Builders<Repo>.Sort.Descending(r => r.Stars).Ascending(r => r.Name),
            _ => Builders<Repo>.Sort.Descending(r => r.PushedAt).Ascending(r => r.Name),
        };

        var total = await context.Repos.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var items = await context.Repos.Find(filter)
            .Sort(sort)
            .Skip((search.Page - 1) * search.PageSize)
            .Limit(search.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Repo>(items, search.Page, search.PageSize, total);
    }

    public async Task SaveSyncAsync(
        Guid userId,
        IReadOnlyCollection<Repo> added,
        IReadOnlyCollection<Repo> updated,
        IReadOnlyCollection<Guid> removedIds,
        CancellationToken cancellationToken)
    {
        var writes = new List<WriteModel<Repo>>(added.Count + updated.Count + 1);

        foreach (var repo in added)
        {
            repo.IncrementVersion();
            writes.Add(new InsertOneModel<Repo>(repo));
        }

        // Sync is the only writer of repository documents, so last writer wins is safe here.
        foreach (var repo in updated)
        {
            repo.IncrementVersion();
            writes.Add(new ReplaceOneModel<Repo>(Filter.Eq(r => r.Id, repo.Id) & Filter.Eq(r => r.UserId, userId), repo));
        }

        if (removedIds.Count > 0)
        {
            writes.Add(new DeleteManyModel<Repo>(Filter.In(r => r.Id, removedIds) & Filter.Eq(r => r.UserId, userId)));
        }

        if (writes.Count == 0)
        {
            return;
        }

        try
        {
            await context.Repos.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, cancellationToken);
        }
        catch (MongoBulkWriteException ex) when (MongoWrites.OnlyDuplicateKeys(ex))
        {
            // A concurrent sync inserted the same repositories; the data is identical.
        }
    }
}
