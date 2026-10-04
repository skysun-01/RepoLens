using System.ComponentModel.DataAnnotations;

namespace RepoLens.Infrastructure.Search;

public enum VectorSearchProvider
{
    /// <summary>Cosine similarity computed in the app. Works on any MongoDB, including a local one; for development and small deployments.</summary>
    InApp,

    /// <summary>MongoDB Atlas Vector Search (<c>$vectorSearch</c>).</summary>
    Atlas,

    /// <summary>Azure Cosmos DB for MongoDB vCore vector search (<c>cosmosSearch</c>).</summary>
    CosmosVCore,
}

public sealed class VectorSearchOptions
{
    public const string SectionName = "VectorSearch";

    public VectorSearchProvider Provider { get; set; } = VectorSearchProvider.InApp;

    [Required]
    public string IndexName { get; set; } = "code_chunks_vector";

    /// <summary>Must match the embedding model's output size (text-embedding-3-small: 1536).</summary>
    [Range(2, 8_192)]
    public int Dimensions { get; set; } = 1_536;

    /// <summary>Atlas: candidates examined per result; higher is more accurate and slower.</summary>
    [Range(1, 100)]
    public int CandidatesPerResult { get; set; } = 20;

    /// <summary>Create the vector index at startup if it is missing (needs index-management rights).</summary>
    public bool CreateIndexIfMissing { get; set; } = true;
}
