using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Application.Source;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries.Processing.Steps;

/// <summary>Downloads the repository archive at the pinned commit and reads its text files.</summary>
public sealed class DownloadSourceStep(IGitHubClient gitHub, SourceSnapshotLoader loader) : ISummaryPipelineStep
{
    public int Order => 200;

    public SummaryStage Stage => SummaryStage.DownloadingSource;

    public int StartProgress => 8;

    public async Task ExecuteAsync(SummaryPipelineContext context, CancellationToken cancellationToken)
    {
        var repo = context.Repo;
        await using var archive = await gitHub.DownloadArchiveAsync(context.AccessToken, repo.Owner, repo.Name, context.CommitSha, cancellationToken);

        var snapshot = loader.Load(archive);
        if (snapshot.Files.Count == 0)
        {
            throw new PermanentJobException(
                "repo_no_source",
                "No readable source or text files were found in this repository (binary, vendored and generated files are skipped).");
        }

        context.Snapshot = snapshot;
    }
}
