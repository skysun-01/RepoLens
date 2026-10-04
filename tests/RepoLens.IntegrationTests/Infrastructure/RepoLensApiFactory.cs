using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RepoLens.Application.Auth;
using RepoLens.Application.Common.Abstractions.GitHub;

namespace RepoLens.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API (in-memory message bus, in-app vector search, GridFS, real MongoDB) with GitHub
/// and the AI model replaced by fakes. Each factory gets its own database.
/// </summary>
public class RepoLensApiFactory(MongoFixture mongo) : WebApplicationFactory<Program>
{
    private static long _nextGitHubId = 10_000;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public FakeGitHubClient GitHub { get; } = new();

    public FakeChatClient Chat { get; } = new();

    public string DatabaseName { get; } = $"repolens_test_{Guid.NewGuid():N}";

    /// <summary>Signs a new user in through the real code exchange and returns a client carrying their access token.</summary>
    public async Task<HttpClient> CreateSignedInClientAsync(string login = "octocat", string? gitHubToken = null)
    {
        var gitHubId = Interlocked.Increment(ref _nextGitHubId);
        string code;
        using (var scope = Services.CreateScope())
        {
            var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();
            code = await signIn.CompleteGitHubSignInAsync(
                new GitHubIdentity(gitHubId, login, "Octo Cat", $"{login}@example.com", null),
                gitHubToken ?? $"gho_{login}_{gitHubId}",
                "read:user,user:email,repo",
                CancellationToken.None);
        }

        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/token", new { code });
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<TokenPairDto>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    /// <summary>Creates a user and returns the one-time sign-in code without exchanging it.</summary>
    public async Task<string> CreateSignInCodeAsync()
    {
        using var scope = Services.CreateScope();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();
        var gitHubId = Interlocked.Increment(ref _nextGitHubId);
        return await signIn.CompleteGitHubSignInAsync(new GitHubIdentity(gitHubId, "coder", null, null, null), $"gho_{gitHubId}", "repo", CancellationToken.None);
    }

    /// <summary>Lets a derived factory change settings, for example to leave GitHub unconfigured.</summary>
    protected virtual void AdjustSettings(Dictionary<string, string?> settings)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        var settings = new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = mongo.ConnectionString,
            ["Mongo:DatabaseName"] = DatabaseName,
            ["Messaging:Provider"] = "InMemory",
            ["Messaging:RetryBaseDelay"] = "00:00:00.200",
            ["Messaging:RetryMaxDelay"] = "00:00:01",
            ["VectorSearch:Provider"] = "InApp",
            ["VectorSearch:Dimensions"] = FakeEmbeddingGenerator.Dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Ai:Provider"] = "OpenAI",
            ["Ai:ApiKey"] = "not-used-in-tests",
            ["GitHub:ClientId"] = "test-client-id",
            ["GitHub:ClientSecret"] = "test-client-secret",
            ["Summaries:Outbox:PollInterval"] = "00:00:01",
            ["Summaries:Outbox:PublishGracePeriod"] = "00:00:00",
            ["RateLimiting:GlobalPerMinute"] = "100000",
            ["RateLimiting:SummariesPerHour"] = "1000",
            ["RateLimiting:QuestionsPerMinute"] = "1000",
            ["RateLimiting:SignInPerMinute"] = "1000",
        };
        AdjustSettings(settings);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitHubClient>();
            services.AddSingleton<IGitHubClient>(GitHub);

            services.RemoveAll<IChatClient>();
            services.AddSingleton<IChatClient>(Chat);

            services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new FakeEmbeddingGenerator());
        });
    }
}
