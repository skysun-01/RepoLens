using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using RepoLens.Application.Common.Abstractions.Search;
using RepoLens.Domain.Indexing;
using RepoLens.Infrastructure.Persistence;

namespace RepoLens.Infrastructure.Search;

/// <summary>
/// Exact nearest-neighbour search computed in the app: loads a repository's vectors once, caches them,
/// and scores every chunk by cosine similarity. No vector index needed, so it runs on a plain local
/// MongoDB. Use Atlas or Cosmos DB vector search for large deployments.
/// </summary>
internal sealed class InAppVectorSearch(MongoContext context, InAppVectorCache vectorCache) : ICodeSearch
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private MemoryCache Cache => vectorCache.Cache;

    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        Guid repoId,
        string commitSha,
        ReadOnlyMemory<float> queryVector,
        int top,
        CancellationToken cancellationToken)
    {
        var vectors = await GetVectorsAsync(repoId, commitSha, cancellationToken);
        if (vectors.Count == 0)
        {
            return [];
        }

        var query = queryVector.ToArray();
        var best = vectors
            .Select(v => (v.Id, Score: CosineSimilarity(query, v.Embedding)))
            .OrderByDescending(x => x.Score)
            .Take(top)
            .ToList();

        var ids = best.Select(b => b.Id).ToList();
        var chunks = (await context.CodeChunks.Find(Builders<CodeChunk>.Filter.In(c => c.Id, ids)).ToListAsync(cancellationToken))
            .ToDictionary(c => c.Id);

        return best
            .Where(b => chunks.ContainsKey(b.Id))
            .Select(b => new ScoredChunk(chunks[b.Id], b.Score))
            .ToList();
    }

    internal static double CosineSimilarity(ReadOnlySpan<float> a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0;
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private async Task<IReadOnlyList<ChunkVector>> GetVectorsAsync(Guid repoId, string commitSha, CancellationToken cancellationToken)
    {
        var key = $"vectors:{repoId:N}:{commitSha}";
        if (Cache.TryGetValue(key, out IReadOnlyList<ChunkVector>? cached) && cached is not null)
        {
            return cached;
        }

        var projection = Builders<CodeChunk>.Projection.Include(c => c.Id).Include(c => c.Embedding);
        var vectors = await context.CodeChunks
            .Find(c => c.RepoId == repoId && c.CommitSha == commitSha)
            .Project<ChunkVector>(projection)
            .ToListAsync(cancellationToken);

        Cache.Set(key, (IReadOnlyList<ChunkVector>)vectors, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheDuration,
            Size = Math.Max(1, vectors.Count),
        });

        return vectors;
    }

    internal sealed class ChunkVector
    {
        // Fetched with a projection: only the id and the vector, not the chunk text.
        [BsonId]
        public string Id { get; set; } = string.Empty;

        [BsonElement("embedding")]
        public float[] Embedding { get; set; } = [];
    }
}
