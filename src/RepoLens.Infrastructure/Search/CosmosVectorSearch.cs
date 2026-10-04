using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using RepoLens.Application.Common.Abstractions.Search;
using RepoLens.Domain.Indexing;
using RepoLens.Infrastructure.Persistence;

namespace RepoLens.Infrastructure.Search;

/// <summary>Azure Cosmos DB for MongoDB (vCore) vector search, filtered to one repository and commit.</summary>
internal sealed class CosmosVectorSearch(MongoContext context, IOptions<VectorSearchOptions> options) : ICodeSearch
{
    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        Guid repoId,
        string commitSha,
        ReadOnlyMemory<float> queryVector,
        int top,
        CancellationToken cancellationToken)
    {
        var search = new BsonDocument("$search", new BsonDocument
        {
            {
                "cosmosSearch", new BsonDocument
                {
                    { "vector", new BsonArray(queryVector.ToArray().Select(v => (double)v)) },
                    { "path", "embedding" },
                    { "k", top * Math.Max(1, options.Value.CandidatesPerResult / 4) },
                    {
                        "filter", new BsonDocument
                        {
                            { "repoId", new BsonDocument("$eq", new BsonBinaryData(repoId, GuidRepresentation.Standard)) },
                            { "commitSha", new BsonDocument("$eq", commitSha) },
                        }
                    },
                }
            },
            { "returnStoredSource", true },
        });

        var project = new BsonDocument("$project", new BsonDocument
        {
            { "score", new BsonDocument("$meta", "searchScore") },
            { "document", "$$ROOT" },
        });

        PipelineDefinition<CodeChunk, BsonDocument> pipeline = new[] { search, project };
        var documents = await context.CodeChunks.Aggregate(pipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);

        // The filter narrows candidates; the post-filter guarantees nothing from another repository slips through.
        return documents
            .Select(d => new ScoredChunk(BsonSerializer.Deserialize<CodeChunk>(d["document"].AsBsonDocument), d["score"].ToDouble()))
            .Where(s => s.Chunk.RepoId == repoId && s.Chunk.CommitSha == commitSha)
            .Take(top)
            .ToList();
    }

    /// <summary>Creates the HNSW vector index and the filter-field index when missing.</summary>
    public static async Task EnsureIndexAsync(MongoContext context, VectorSearchOptions settings, CancellationToken cancellationToken)
    {
        using var cursor = await context.CodeChunks.Indexes.ListAsync(cancellationToken);
        var existing = await cursor.ToListAsync(cancellationToken);
        if (existing.Any(i => i.GetValue("name", string.Empty).AsString == settings.IndexName))
        {
            return;
        }

        var command = new BsonDocument
        {
            { "createIndexes", MongoContext.Names.CodeChunks },
            {
                "indexes", new BsonArray
                {
                    new BsonDocument
                    {
                        { "name", settings.IndexName },
                        { "key", new BsonDocument("embedding", "cosmosSearch") },
                        {
                            "cosmosSearchOptions", new BsonDocument
                            {
                                { "kind", "vector-hnsw" },
                                { "m", 16 },
                                { "efConstruction", 64 },
                                { "similarity", "COS" },
                                { "dimensions", settings.Dimensions },
                            }
                        },
                    },
                }
            },
        };

        await context.Database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken);
    }
}
