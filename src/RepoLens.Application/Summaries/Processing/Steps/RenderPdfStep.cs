using RepoLens.Application.Common.Abstractions.Documents;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries.Processing.Steps;

public sealed class RenderPdfStep(ISummaryPdfRenderer renderer, TimeProvider timeProvider) : ISummaryPipelineStep
{
    public int Order => 500;

    public SummaryStage Stage => SummaryStage.RenderingPdf;

    public int StartProgress => 90;

    public Task ExecuteAsync(SummaryPipelineContext context, CancellationToken cancellationToken)
    {
        var snapshot = context.RequireSnapshot();
        var repo = context.Repo;
        var info = new SummaryDocumentInfo(
            repo.FullName,
            repo.Description,
            repo.HtmlUrl,
            context.Job.Branch,
            context.CommitSha,
            snapshot.PrimaryLanguage ?? repo.Language,
            snapshot.Files.Count,
            timeProvider.GetUtcNow());

        context.Pdf = renderer.Render(context.RequireSummary(), info);
        return Task.CompletedTask;
    }
}
