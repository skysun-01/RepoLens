using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RepoLens.Application.Auth;
using RepoLens.Application.Common.Abstractions.Messaging;
using RepoLens.Application.Indexing;
using RepoLens.Application.Questions;
using RepoLens.Application.Repos;
using RepoLens.Application.Source;
using RepoLens.Application.Summaries;
using RepoLens.Application.Summaries.Generation;
using RepoLens.Application.Summaries.Outbox;
using RepoLens.Application.Summaries.Processing;
using RepoLens.Application.Summaries.Processing.Steps;
using RepoLens.Application.Users;

namespace RepoLens.Application;

public static class DependencyInjection
{
    /// <summary>Use cases used by the API: sign-in, repositories, summary requests and questions.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddValidatedOptions<AuthOptions>(AuthOptions.SectionName);
        services.AddValidatedOptions<RepoOptions>(RepoOptions.SectionName);
        services.AddValidatedOptions<QuestionOptions>(QuestionOptions.SectionName);
        services.AddValidatedOptions<OutboxOptions>(OutboxOptions.SectionName);
        services.AddValidatedOptions<SummaryProcessingOptions>(SummaryProcessingOptions.SectionName);

        services.AddScoped<SignInService>();
        services.AddScoped<TokenService>();
        services.AddScoped<UserService>();
        services.AddScoped<GitHubTokenAccessor>();

        services.AddScoped<RepoSyncService>();
        services.AddScoped<RepoQueryService>();
        services.AddScoped<RemovedRepoCleaner>();

        services.AddScoped<SummaryRequestService>();
        services.AddScoped<SummaryQueryService>();
        services.AddScoped<SummaryOutboxDispatcher>();

        services.AddScoped<QuestionAnsweringService>();
        services.AddScoped<ConversationService>();

        return services;
    }

    /// <summary>
    /// The summary pipeline: consumes <see cref="GenerateSummaryCommand"/>. Registered by the Worker,
    /// and by the API when it runs with the in-memory message bus.
    /// </summary>
    public static IServiceCollection AddSummaryProcessing(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddValidatedOptions<SourceOptions>(SourceOptions.SectionName);
        services.AddValidatedOptions<IndexingOptions>(IndexingOptions.SectionName);
        services.AddValidatedOptions<SummaryGenerationOptions>(SummaryGenerationOptions.SectionName);
        services.AddValidatedOptions<SummaryProcessingOptions>(SummaryProcessingOptions.SectionName);

        services.TryAddScoped<GitHubTokenAccessor>();
        services.TryAddSingleton<ICodeFileFilter, DefaultCodeFileFilter>();
        services.TryAddSingleton<SourceSnapshotLoader>();
        services.TryAddSingleton<CodeChunker>();
        services.TryAddSingleton<ModulePlanner>();
        services.TryAddScoped<CodeIndexer>();
        services.TryAddScoped<ISummaryGenerator, RepoSummaryGenerator>();

        services.AddScoped<ISummaryPipelineStep, ResolveCommitStep>();
        services.AddScoped<ISummaryPipelineStep, DownloadSourceStep>();
        services.AddScoped<ISummaryPipelineStep, IndexCodeStep>();
        services.AddScoped<ISummaryPipelineStep, GenerateSummaryStep>();
        services.AddScoped<ISummaryPipelineStep, RenderPdfStep>();
        services.AddScoped<ISummaryPipelineStep, StorePdfStep>();
        services.TryAddScoped<SummaryPipeline>();

        services.TryAddScoped<IMessageHandler<GenerateSummaryCommand>, SummaryJobProcessor>();
        return services;
    }

    private static void AddValidatedOptions<TOptions>(this IServiceCollection services, string sectionName)
        where TOptions : class
    {
        if (services.Any(d => d.ServiceType == typeof(OptionsMarker<TOptions>)))
        {
            return;
        }

        services.AddSingleton<OptionsMarker<TOptions>>();
        services.AddOptions<TOptions>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    /// <summary>Prevents binding the same options twice when both registration methods run.</summary>
    private sealed class OptionsMarker<TOptions>;
}
