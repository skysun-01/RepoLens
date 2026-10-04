using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;

namespace RepoLens.Infrastructure.AI;

public static class AiServiceCollectionExtensions
{
    public const string TelemetrySourceName = "RepoLens.AI";

    /// <summary>Registers <see cref="IChatClient"/> and <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> for the configured provider.</summary>
    public static IServiceCollection AddAi(this IServiceCollection services)
    {
        services.AddOptions<AiOptions>()
            .BindConfiguration(AiOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp => CreateClient(sp.GetRequiredService<IOptions<AiOptions>>().Value));

        services.AddChatClient(sp =>
                sp.GetRequiredService<OpenAIClient>()
                    .GetChatClient(sp.GetRequiredService<IOptions<AiOptions>>().Value.ChatModel)
                    .AsIChatClient())
            .UseOpenTelemetry(sourceName: TelemetrySourceName);

        services.AddEmbeddingGenerator(sp =>
            {
                var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
                return sp.GetRequiredService<OpenAIClient>()
                    .GetEmbeddingClient(options.EmbeddingModel)
                    .AsIEmbeddingGenerator(options.EmbeddingDimensions);
            })
            .UseOpenTelemetry(sourceName: TelemetrySourceName);

        return services;
    }

    internal static OpenAIClient CreateClient(AiOptions options)
    {
        var clientOptions = new OpenAIClientOptions { NetworkTimeout = options.NetworkTimeout };

        if (options.Provider == AiProvider.AzureOpenAI)
        {
            clientOptions.Endpoint = AzureV1Endpoint(options.Endpoint!);

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                // Managed identity / Entra ID: the identity needs the "Cognitive Services OpenAI User" role.
                // The token-policy constructor is marked experimental in the OpenAI SDK; it is the documented
                // way to use Entra ID with the Azure OpenAI v1 endpoint.
                var tokenPolicy = new BearerTokenPolicy(new DefaultAzureCredential(), "https://cognitiveservices.azure.com/.default");
#pragma warning disable OPENAI001
                return new OpenAIClient(tokenPolicy, clientOptions);
#pragma warning restore OPENAI001
            }
        }
        else if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            clientOptions.Endpoint = new Uri(options.Endpoint);
        }

        return new OpenAIClient(new ApiKeyCredential(options.ApiKey!), clientOptions);
    }

    /// <summary>Accepts the resource URL in any common form and returns the <c>/openai/v1/</c> endpoint.</summary>
    internal static Uri AzureV1Endpoint(string endpoint)
    {
        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed + "/");
        }

        if (trimmed.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^"/openai".Length];
        }

        return new Uri($"{trimmed}/openai/v1/");
    }
}
