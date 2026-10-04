using RepoLens.Domain.Indexing;

namespace RepoLens.Application.Common.Abstractions.Search;

/// <summary>Writes and removes code chunks (with their embeddings).</summary>
public interface ICodeChunkStore
{
    Task UpsertAsync(IReadOnlyCollection<CodeChunk> chunks, CancellationToken cancellationToken);

    Task<long> CountAsync(Guid repoId, string commitSha, CancellationToken cancellationToken);

    /// <summary>Deletes a repository's chunks, optionally keeping those of one commit.</summary>
    Task DeleteAsync(Guid repoId, string? keepCommitSha, CancellationToken cancellationToken);

    Task DeleteManyAsync(IReadOnlyCollection<Guid> repoIds, CancellationToken cancellationToken);
}

/// <summary>
/// Finds the chunks most similar to a query vector. Implementations: MongoDB Atlas $vectorSearch,
/// Azure Cosmos DB for MongoDB vCore, or in-app cosine similarity for a plain local MongoDB.
/// </summary>
public interface ICodeSearch
{
    Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        Guid repoId,
        string commitSha,
        ReadOnlyMemory<float> queryVector,
        int top,
        CancellationToken cancellationToken);
}

public sealed record ScoredChunk(CodeChunk Chunk, double Score);
