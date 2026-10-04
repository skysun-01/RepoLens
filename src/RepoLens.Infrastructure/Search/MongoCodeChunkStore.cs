using MongoDB.Driver;
using RepoLens.Application.Common.Abstractions.Search;
using RepoLens.Domain.Indexing;
using RepoLens.Infrastructure.Persistence;

namespace RepoLens.Infrastructure.Search;

internal sealed class MongoCodeChunkStore(MongoContext context) : ICodeChunkStore
{
    private static FilterDefinitionBuilder<CodeChunk> Filter => Builders<CodeChunk>.Filter;

    public async Task UpsertAsync(IReadOnlyCollection<CodeChunk> chunks, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        // Ids are deterministic, so a retried batch overwrites instead of duplicating.
        var writes = chunks
            .Select(chunk => new ReplaceOneModel<CodeChunk>(Filter.Eq(c => c.Id, chunk.Id), chunk) { IsUpsert = true })
            .ToList();

        await context.CodeChunks.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, cancellationToken);
    }

    public Task<long> CountAsync(Guid repoId, string commitSha, CancellationToken cancellationToken) =>
        context.CodeChunks.CountDocumentsAsync(c => c.RepoId == repoId && c.CommitSha == commitSha, cancellationToken: cancellationToken);

    public Task DeleteAsync(Guid repoId, string? keepCommitSha, CancellationToken cancellationToken)
    {
        var filter = Filter.Eq(c => c.RepoId, repoId);
        if (keepCommitSha is not null)
        {
            filter &= Filter.Ne(c => c.CommitSha, keepCommitSha);
        }

        return context.CodeChunks.DeleteManyAsync(filter, cancellationToken);
    }

    public Task DeleteManyAsync(IReadOnlyCollection<Guid> repoIds, CancellationToken cancellationToken) =>
        repoIds.Count == 0
            ? Task.CompletedTask
            : context.CodeChunks.DeleteManyAsync(Filter.In(c => c.RepoId, repoIds), cancellationToken);
}
