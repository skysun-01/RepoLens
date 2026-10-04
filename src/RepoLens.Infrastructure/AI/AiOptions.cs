using System.ComponentModel.DataAnnotations;

namespace RepoLens.Infrastructure.AI;

public enum AiProvider
{
    /// <summary>Azure OpenAI through its OpenAI-compatible v1 endpoint. Model names are deployment names.</summary>
    AzureOpenAI,

    /// <summary>api.openai.com, or any OpenAI-compatible server when <see cref="AiOptions.Endpoint"/> is set.</summary>
    OpenAI,
}

public sealed class AiOptions : IValidatableObject
{
    public const string SectionName = "Ai";

    public AiProvider Provider { get; set; } = AiProvider.AzureOpenAI;

    /// <summary>Azure: <c>https://your-resource.openai.azure.com</c>. OpenAI: optional custom base URL.</summary>
    public string? Endpoint { get; set; }

    /// <summary>API key. For Azure it may be left empty to use managed identity (Entra ID) instead.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Chat model (Azure: deployment name) used for summaries and answers.</summary>
    [Required]
    public string ChatModel { get; set; } = "gpt-5-mini";

    /// <summary>Embedding model (Azure: deployment name). Changing it requires re-indexing repositories.</summary>
    [Required]
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    [Range(2, 8_192)]
    public int EmbeddingDimensions { get; set; } = 1_536;

    [Range(typeof(TimeSpan), "00:00:10", "00:30:00")]
    public TimeSpan NetworkTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider == AiProvider.AzureOpenAI && string.IsNullOrWhiteSpace(Endpoint))
        {
            yield return new ValidationResult("Ai:Endpoint is required for Azure OpenAI (https://your-resource.openai.azure.com).", [nameof(Endpoint)]);
        }

        if (Provider == AiProvider.OpenAI && string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return new ValidationResult("Ai:ApiKey is required for the OpenAI provider. Set it with 'dotnet user-secrets'.", [nameof(ApiKey)]);
        }

        if (!string.IsNullOrWhiteSpace(Endpoint) && !Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
        {
            yield return new ValidationResult("Ai:Endpoint must be an absolute URL.", [nameof(Endpoint)]);
        }
    }
}
