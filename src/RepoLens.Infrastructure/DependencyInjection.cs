using System.Net;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Polly;
using Polly.Timeout;
using QuestPDF.Infrastructure;
using RepoLens.Application.Common.Abstractions.Documents;
using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Abstractions.Search;
using RepoLens.Application.Common.Abstractions.Security;
using RepoLens.Application.Common.Abstractions.Storage;
using RepoLens.Infrastructure.AI;
using RepoLens.Infrastructure.Documents;
using RepoLens.Infrastructure.GitHub;
using RepoLens.Infrastructure.Messaging;
using RepoLens.Infrastructure.Persistence;
using RepoLens.Infrastructure.Persistence.Repositories;
using RepoLens.Infrastructure.Search;
using RepoLens.Infrastructure.Security;
using RepoLens.Infrastructure.Storage;

namespace RepoLens.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Everything the API and the Worker share: MongoDB, GitHub, messaging, AI, search, PDF and encryption.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddMongo();
        services.AddGitHub();
        services.AddMessaging(configuration);
        services.AddAi();
        services.AddSearch(configuration);
        services.AddFileStorage();
        services.AddPdf(configuration);
        services.AddDataProtectionWithMongoKeys(configuration);

        services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb", tags: ["ready"]);
        return services;
    }

    /// <summary>JWT access-token issuing; only the API needs it.</summary>
    public static IServiceCollection AddJwtAccessTokens(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        return services;
    }

    private static void AddMongo(this IServiceCollection services)
    {
        MongoMappings.Register();

        services.AddOptions<MongoOptions>()
            .BindConfiguration(MongoOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<IMongoClient>(sp =>
        {
            var settings = MongoClientSettings.FromConnectionString(sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString);
            settings.ApplicationName = "RepoLens";
            return new MongoClient(settings);
        });
        services.TryAddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(sp.GetRequiredService<IOptions<MongoOptions>>().Value.DatabaseName));
        services.TryAddSingleton<MongoContext>();

        // Runs before other hosted services so indexes exist before any traffic.
        services.AddHostedService<MongoIndexInitializer>();

        services.TryAddSingleton<IUserRepository, MongoUserRepository>();
        services.TryAddSingleton<IRepoRepository, MongoRepoRepository>();
        services.TryAddSingleton<IRepoIndexRepository, MongoRepoIndexRepository>();
        services.TryAddSingleton<ISummaryJobRepository, MongoSummaryJobRepository>();
        services.TryAddSingleton<IConversationRepository, MongoConversationRepository>();
        services.TryAddSingleton<IAuthCodeRepository, MongoAuthCodeRepository>();
        services.TryAddSingleton<IRefreshTokenRepository, MongoRefreshTokenRepository>();
    }

    private static void AddGitHub(this IServiceCollection services)
    {
        services.AddOptions<GitHubOptions>()
            .BindConfiguration(GitHubOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IGitHubClient, GitHubClient>((sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<GitHubOptions>>().Value;
                http.BaseAddress = new Uri(options.ApiBaseUrl.EndsWith('/') ? options.ApiBaseUrl : options.ApiBaseUrl + "/");
                http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
                http.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler(resilience =>
            {
                // Archive downloads of large repositories take a while.
                resilience.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
                resilience.Retry.MaxRetryAttempts = 3;
                resilience.Retry.ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome));
            });
    }

    /// <summary>Retry network errors, timeouts and 5xx. Not 429/403: GitHub rate limits reset in minutes, not seconds.</summary>
    private static bool IsTransient(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Exception: HttpRequestException or TimeoutRejectedException } => true,
        { Result: { } response } => (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout,
        _ => false,
    };

    private static void AddSearch(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VectorSearchOptions>()
            .BindConfiguration(VectorSearchOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<ICodeChunkStore, MongoCodeChunkStore>();

        var provider = configuration.GetSection(VectorSearchOptions.SectionName).GetValue<VectorSearchProvider?>(nameof(VectorSearchOptions.Provider))
            ?? VectorSearchProvider.InApp;

        switch (provider)
        {
            case VectorSearchProvider.Atlas:
                services.TryAddSingleton<ICodeSearch, AtlasVectorSearch>();
                break;
            case VectorSearchProvider.CosmosVCore:
                services.TryAddSingleton<ICodeSearch, CosmosVectorSearch>();
                break;
            default:
                services.TryAddSingleton<InAppVectorCache>();
                services.TryAddSingleton<ICodeSearch, InAppVectorSearch>();
                break;
        }

        services.AddHostedService<VectorIndexInitializer>();
    }

    private static void AddFileStorage(this IServiceCollection services)
    {
        services.AddOptions<GridFsOptions>().BindConfiguration(GridFsOptions.SectionName);
        services.TryAddSingleton<IFileStorage, GridFsFileStorage>();
    }

    private static void AddPdf(this IServiceCollection services, IConfiguration configuration)
    {
        var license = configuration.GetSection(PdfOptions.SectionName).GetValue<string>(nameof(PdfOptions.QuestPdfLicense)) ?? "Community";
        QuestPDF.Settings.License = Enum.Parse<LicenseType>(license, ignoreCase: true);
        services.TryAddSingleton<ISummaryPdfRenderer, QuestPdfSummaryRenderer>();
    }

    private static void AddDataProtectionWithMongoKeys(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<MongoXmlRepository>();
        services.TryAddSingleton<ITokenProtector, DataProtectionTokenProtector>();
        services.TryAddSingleton<ISecretTokenGenerator, SecretTokenGenerator>();

        var dataProtection = services.AddDataProtection().SetApplicationName("RepoLens");
        services.AddOptions<KeyManagementOptions>()
            .Configure<MongoXmlRepository>((options, repository) => options.XmlRepository = repository);

        // In Azure, wrap the key ring with a Key Vault key so keys are encrypted at rest in MongoDB.
        var keyVaultKeyId = configuration["DataProtection:KeyVaultKeyId"];
        if (!string.IsNullOrWhiteSpace(keyVaultKeyId))
        {
            dataProtection.ProtectKeysWithAzureKeyVault(new Uri(keyVaultKeyId), new DefaultAzureCredential());
        }
    }
}
