namespace RepoLens.Domain.Summaries;

/// <summary>Finer-grained progress inside <see cref="SummaryStatus.Processing"/>, shown in the UI.</summary>
public enum SummaryStage
{
    Queued,
    Starting,
    ResolvingCommit,
    DownloadingSource,
    IndexingCode,
    GeneratingSummary,
    RenderingPdf,
    StoringPdf,
    Completed,
    Failed,
}
