using RepoLens.Application.Source;
using RepoLens.Domain.Repos;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries.Generation;

/// <summary>Turns a repository snapshot into a structured onboarding summary.</summary>
public interface ISummaryGenerator
{
    /// <param name="reportProgress">Receives the completed fraction, 0 to 1.</param>
    Task<RepoSummary> GenerateAsync(
        SummaryGenerationRequest request,
        Func<double, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken);
}

public sealed record SummaryGenerationRequest(Repo Repo, string CommitSha, SourceSnapshot Snapshot);
