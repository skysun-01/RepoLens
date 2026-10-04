using Microsoft.Extensions.AI;

namespace RepoLens.Application.Indexing;

internal static class EmbeddingModel
{
    /// <summary>
    /// The embedding model's id. Stored with each index: vectors from different models are not
    /// comparable, so a model change means the repository must be indexed again.
    /// </summary>
    public static string IdOf(IEmbeddingGenerator<string, Embedding<float>> generator) =>
        generator.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId is { Length: > 0 } id ? id : "default";
}
