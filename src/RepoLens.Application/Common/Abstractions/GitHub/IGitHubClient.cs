using RepoLens.Domain.Repos;

namespace RepoLens.Application.Common.Abstractions.GitHub;

/// <summary>The subset of the GitHub REST API RepoLens needs, called with the user's own token.</summary>
public interface IGitHubClient
{
    /// <summary>Every repository the user can access: owned, collaborator and organization member.</summary>
    Task<IReadOnlyList<RepoDetails>> GetRepositoriesAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>SHA of the latest commit on <paramref name="branch"/>.</summary>
    Task<string> GetBranchHeadShaAsync(string accessToken, string owner, string repo, string branch, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the repository as a zip archive at <paramref name="commitSha"/>: one request instead
    /// of one per file. Returns a seekable stream backed by a temporary file; the caller disposes it.
    /// </summary>
    Task<Stream> DownloadArchiveAsync(string accessToken, string owner, string repo, string commitSha, CancellationToken cancellationToken);
}
