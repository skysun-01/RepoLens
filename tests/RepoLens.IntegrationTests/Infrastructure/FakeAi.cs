using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace RepoLens.IntegrationTests.Infrastructure;

/// <summary>
/// Deterministic stand-in for the chat model. Structured-output requests get valid JSON for the
/// requested schema (repository summary or module digest); plain requests get an answer with citations.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    public const string Answer = "Orders are created by `OrderService.Create` [1], which `Program.cs` calls for POST /orders [2].";

    private const string SummaryJson = """
        {
          "overview": "Shop is a sample e-commerce API that creates orders.",
          "audience": "Developers building the storefront.",
          "techStack": [ { "name": "ASP.NET Core", "category": "Framework", "purpose": "HTTP API" } ],
          "architecture": "Minimal API endpoints in Program.cs call services in the Orders folder.",
          "modules": [ { "path": "src/Api", "responsibility": "HTTP API and order logic", "keyFiles": [ "src/Api/Program.cs" ] } ],
          "keyFlows": [ { "name": "Create order", "summary": "POST /orders creates an order.", "steps": [ "Program.cs maps POST /orders", "OrderService.Create builds the order" ] } ],
          "entryPoints": [ { "path": "src/Api/Program.cs", "description": "Starts the web host." } ],
          "dataModel": "Order is a record with an id.",
          "configuration": [ { "name": "ConnectionStrings:Db", "description": "Database connection string." } ],
          "buildAndRun": [ { "title": "Run the API", "command": "dotnet run --project src/Api" } ],
          "whereToStart": [ "src/Api/Program.cs", "src/Api/Orders/OrderService.cs" ],
          "glossary": [ { "term": "Order", "definition": "A customer's purchase." } ]
        }
        """;

    private const string DigestJson = """
        {
          "purpose": "HTTP API and order logic.",
          "keyFiles": [ "src/Api/Program.cs: entry point" ],
          "flows": [ "POST /orders goes from Program.cs to OrderService.Create" ],
          "technologies": [ "ASP.NET Core" ],
          "configuration": [ "ConnectionStrings:Db" ],
          "dataModel": [ "Order" ]
        }
        """;

    private int _structuredCalls;

    /// <summary>When set, summary generation waits for it; lets tests observe a job while it is running.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public int StructuredCalls => Volatile.Read(ref _structuredCalls);

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        string text;
        if (options?.ResponseFormat is ChatResponseFormatJson json)
        {
            Interlocked.Increment(ref _structuredCalls);
            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            var schema = json.Schema?.GetRawText() ?? string.Empty;
            text = schema.Contains("overview", StringComparison.OrdinalIgnoreCase) ? SummaryJson : DigestJson;
        }
        else
        {
            text = Answer;
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = ChatFinishReason.Stop };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var words = Answer.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, i == 0 ? words[i] : " " + words[i]);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}

/// <summary>Bag-of-words hashing into 64 dimensions: similar text gets similar vectors, deterministically.</summary>
public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int Dimensions = 64;
    public const string ModelId = "fake-embedding-v1";

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(v => new Embedding<float>(Vectorize(v)))));

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(EmbeddingGeneratorMetadata) ? new EmbeddingGeneratorMetadata("fake", null, ModelId, Dimensions)
        : serviceKey is null && serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
    }

    private static float[] Vectorize(string text)
    {
        var vector = new float[Dimensions];
        foreach (var word in text.ToLowerInvariant().Split([' ', '\n', '.', '(', ')', '/', '"', ':', ';', ',', '`'], StringSplitOptions.RemoveEmptyEntries))
        {
            uint hash = 2166136261;
            foreach (var c in word)
            {
                hash = (hash ^ c) * 16777619;
            }

            vector[hash % Dimensions] += 1;
        }

        return vector;
    }
}
