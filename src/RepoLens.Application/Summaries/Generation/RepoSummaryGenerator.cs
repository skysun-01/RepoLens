using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoLens.Application.Summaries.Processing;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries.Generation;

/// <summary>
/// Map → reduce summary generation. Small repositories go straight to one reduce call. Larger ones
/// get one map call per module (in parallel, bounded), then a reduce call that combines the module
/// notes with the README, manifests, configuration and file tree into a <see cref="RepoSummary"/>.
/// </summary>
public sealed partial class RepoSummaryGenerator(
    IChatClient chatClient,
    ModulePlanner planner,
    IOptions<SummaryGenerationOptions> options,
    ILogger<RepoSummaryGenerator> logger) : ISummaryGenerator
{
    private const double MapShare = 0.7;

    public async Task<RepoSummary> GenerateAsync(
        SummaryGenerationRequest request,
        Func<double, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var plan = planner.Plan(request.Snapshot);
        IReadOnlyList<(string Module, ModuleDigest Digest)> digests = [];

        if (!plan.SinglePass)
        {
            LogMapPhase(request.Repo.FullName, plan.Modules.Count);
            digests = await MapAsync(request, plan, reportProgress, cancellationToken);
        }

        await reportProgress(plan.SinglePass ? 0.1 : MapShare, cancellationToken);

        var messages = SummaryPrompts.Reduce(request.Repo, request.CommitSha, request.Snapshot, plan, digests, settings);
        var summary = await AskForJsonAsync<RepoSummary>(messages, settings.ReduceMaxOutputTokens, cancellationToken);

        await reportProgress(1, cancellationToken);
        return SummaryNormalizer.Normalize(summary);
    }

    private async Task<IReadOnlyList<(string Module, ModuleDigest Digest)>> MapAsync(
        SummaryGenerationRequest request,
        SummaryPlan plan,
        Func<double, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(options.Value.MapConcurrency);
        var completed = 0;

        var tasks = plan.Modules.Select(async module =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var digest = await AskForJsonAsync<ModuleDigest>(
                    SummaryPrompts.Map(request.Repo, module),
                    options.Value.MapMaxOutputTokens,
                    cancellationToken);

                var done = Interlocked.Increment(ref completed);
                await reportProgress(MapShare * done / plan.Modules.Count, cancellationToken);
                return (module.Name, digest);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    /// <summary>Asks for structured JSON matching <typeparamref name="T"/>, retrying if the model returns invalid JSON.</summary>
    private async Task<T> AskForJsonAsync<T>(IReadOnlyList<ChatMessage> messages, int maxOutputTokens, CancellationToken cancellationToken)
        where T : class
    {
        var attempts = options.Value.MaxModelAttempts;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var chatOptions = new ChatOptions { MaxOutputTokens = maxOutputTokens };
            var response = await chatClient.GetResponseAsync<T>(messages, chatOptions, useJsonSchemaResponseFormat: true, cancellationToken);

            if (response.TryGetResult(out var result) && result is not null)
            {
                return result;
            }

            LogInvalidOutput(typeof(T).Name, attempt, attempts, response.FinishReason?.Value ?? "unknown");
        }

        throw new PermanentJobException(
            "invalid_ai_output",
            "The AI model did not return a usable summary. Try again, or use a larger model.");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Summarizing {Repo} with a map phase over {ModuleCount} modules")]
    private partial void LogMapPhase(string repo, int moduleCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model returned invalid {Type} JSON (attempt {Attempt}/{Attempts}, finish reason {FinishReason})")]
    private partial void LogInvalidOutput(string type, int attempt, int attempts, string finishReason);
}
