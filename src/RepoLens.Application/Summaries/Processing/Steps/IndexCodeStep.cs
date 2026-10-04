using RepoLens.Application.Indexing;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries.Processing.Steps;

/// <summary>Builds the search index that question answering uses.</summary>
public sealed class IndexCodeStep(CodeIndexer indexer) : ISummaryPipelineStep
{
    public int Order => 300;

    public SummaryStage Stage => SummaryStage.IndexingCode;

    public int StartProgress => 15;

    public Task ExecuteAsync(SummaryPipelineContext context, CancellationToken cancellationToken) =>
        indexer.IndexAsync(context.Repo, context.CommitSha, context.RequireSnapshot(), context.ReportProgress, cancellationToken);
}
