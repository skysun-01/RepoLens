using RepoLens.Application.Summaries.Generation;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries.Processing.Steps;

public sealed class GenerateSummaryStep(ISummaryGenerator generator) : ISummaryPipelineStep
{
    public int Order => 400;

    public SummaryStage Stage => SummaryStage.GeneratingSummary;

    public int StartProgress => 40;

    public async Task ExecuteAsync(SummaryPipelineContext context, CancellationToken cancellationToken)
    {
        var request = new SummaryGenerationRequest(context.Repo, context.CommitSha, context.RequireSnapshot());
        context.Summary = await generator.GenerateAsync(request, context.ReportProgress, cancellationToken);
    }
}
