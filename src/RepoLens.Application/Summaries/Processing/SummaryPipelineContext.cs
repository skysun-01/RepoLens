using RepoLens.Application.Source;
using RepoLens.Domain.Repos;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries.Processing;

/// <summary>State passed from one pipeline step to the next while one job is processed.</summary>
public sealed class SummaryPipelineContext(SummaryJob job, Repo repo, string accessToken)
{
    public SummaryJob Job { get; } = job;

    public Repo Repo { get; } = repo;

    /// <summary>The user's decrypted GitHub token. Kept in memory only.</summary>
    public string AccessToken { get; } = accessToken;

    public SourceSnapshot? Snapshot { get; set; }

    public RepoSummary? Summary { get; set; }

    public byte[]? Pdf { get; set; }

    public StoredFile? StoredPdf { get; set; }

    /// <summary>Reports progress inside the current step as a fraction from 0 to 1. Set by the pipeline.</summary>
    public Func<double, CancellationToken, Task> ReportProgress { get; internal set; } = (_, _) => Task.CompletedTask;

    public string CommitSha => Job.CommitSha ?? throw new InvalidOperationException("The commit has not been resolved yet.");

    public SourceSnapshot RequireSnapshot() => Snapshot ?? throw new InvalidOperationException("The source has not been downloaded yet.");

    public RepoSummary RequireSummary() => Summary ?? throw new InvalidOperationException("The summary has not been generated yet.");

    public byte[] RequirePdf() => Pdf ?? throw new InvalidOperationException("The PDF has not been rendered yet.");

    public StoredFile RequireStoredPdf() => StoredPdf ?? throw new InvalidOperationException("The PDF has not been stored yet.");
}
