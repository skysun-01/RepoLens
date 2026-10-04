using System.ComponentModel.DataAnnotations;

namespace RepoLens.Application.Indexing;

public sealed class IndexingOptions
{
    public const string SectionName = "Indexing";

    /// <summary>Target chunk size in characters. Chunks end early at a natural code boundary.</summary>
    [Range(200, 8_000)]
    public int MaxChunkChars { get; set; } = 1_500;

    /// <summary>Characters repeated at the start of the next chunk so context is not cut mid-thought.</summary>
    [Range(0, 2_000)]
    public int OverlapChars { get; set; } = 150;

    /// <summary>Upper bound on chunks per repository, keeping embedding cost predictable.</summary>
    [Range(1, 100_000)]
    public int MaxChunks { get; set; } = 4_000;

    [Range(1, 2_048)]
    public int EmbeddingBatchSize { get; set; } = 64;
}
