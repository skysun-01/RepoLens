using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using RepoLens.Application.Common.Abstractions.Search;
using RepoLens.Domain.Indexing;
using RepoLens.Infrastructure.Persistence;

namespace RepoLens.Infrastructure.Search;

/// <summary>MongoDB Atlas Vector Search, filtered to one repository and commit.</summary>
internal sealed class AtlasVectorSearch(MongoContext context, IOptions<VectorSearchOptions> options) : ICodeSearch
{
    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        Guid repoId,
        string commitSha,
        ReadOnlyMemory<float> queryVector,
        int top,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var vectorSearch = new BsonDocument("$vectorSearch", new BsonDocument
        {
            { "index", settings.IndexName },
            { "path", "embedding" },
            { "queryVector", new BsonArray(queryVector.ToArray().Select(v => (double)v)) },
            { "numCandidates", top * settings.CandidatesPerResult },
            { "limit", top },
            {
                "filter", new BsonDocument
                {
                    { "repoId", new BsonDocument("$eq", new BsonBinaryData(repoId, GuidRepresentation.Standard)) },
                    { "commitSha", new BsonDocument("$eq", commitSha) },
                }
            },
        });

        var withScore = new BsonDocument("$set", new BsonDocument("_score", new BsonDocument("$meta", "vectorSearchScore")));

        PipelineDefinition<CodeChunk, BsonDocument> pipeline = new[] { vectorSearch, withScore };
        var documents = await context.CodeChunks.Aggregate(pipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);

        return documents
            .Select(d => new ScoredChunk(BsonSerializer.Deserialize<CodeChunk>(d), d["_score"].ToDouble()))
            .ToList();
    }

    /// <summary>Creates the vector search index (with repoId and commitSha filter fields) when it is missing.</summary>
    public static async Task EnsureIndexAsync(MongoContext context, VectorSearchOptions settings, CancellationToken cancellationToken)
    {
        using var cursor = await context.CodeChunks.SearchIndexes.ListAsync(settings.IndexName, cancellationToken: cancellationToken);
        if ((await cursor.ToListAsync(cancellationToken)).Count > 0)
        {
            return;
        }

        var definition = new BsonDocument("fields", new BsonArray
        {
            new BsonDocument { { "type", "vector" }, { "path", "embedding" }, { "numDimensions", settings.Dimensions }, { "similarity", "cosine" } },
            new BsonDocument { { "type", "filter" }, { "path", "repoId" } },
            new BsonDocument { { "type", "filter" }, { "path", "commitSha" } },
        });

        await context.CodeChunks.SearchIndexes.CreateOneAsync(
            new CreateSearchIndexModel(settings.IndexName, SearchIndexType.VectorSearch, definition),
            cancellationToken);
    }
}
