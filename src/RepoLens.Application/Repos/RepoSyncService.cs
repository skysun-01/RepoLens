using Microsoft.Extensions.Logging;
using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Abstractions.Search;
using RepoLens.Application.Common.Errors;
using RepoLens.Application.Users;
using RepoLens.Domain.Repos;

namespace RepoLens.Application.Repos;

/// <summary>Mirrors the user's GitHub repository list into the database.</summary>
public sealed partial class RepoSyncService(
    IRepoRepository repos,
    IUserRepository users,
    GitHubTokenAccessor tokenAccessor,
    IGitHubClient gitHub,
    RemovedRepoCleaner cleaner,
    TimeProvider timeProvider,
    ILogger<RepoSyncService> logger)
{
    public async Task<RepoSyncResultDto> SyncAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (user, accessToken) = await tokenAccessor.GetAsync(userId, cancellationToken);
        var remote = await gitHub.GetRepositoriesAsync(accessToken, cancellationToken);
        var existing = (await repos.ListAllAsync(userId, cancellationToken)).ToDictionary(r => r.GitHubRepoId);

        var now = timeProvider.GetUtcNow();
        var added = new List<Repo>();
        var updated = new List<Repo>();
        var seen = new HashSet<long>();

        foreach (var details in remote)
        {
            if (!seen.Add(details.GitHubRepoId))
            {
                continue;
            }

            if (existing.TryGetValue(details.GitHubRepoId, out var repo))
            {
                if (repo.UpdateFrom(details, now))
                {
                    updated.Add(repo);
                }
            }
            else
            {
                added.Add(Repo.Create(userId, details, now));
            }
        }

        var removedIds = existing.Values.Where(r => !seen.Contains(r.GitHubRepoId)).Select(r => r.Id).ToList();

        await repos.SaveSyncAsync(userId, added, updated, removedIds, cancellationToken);
        await cleaner.CleanUpAsync(removedIds, cancellationToken);

        user.MarkReposSynced(now);
        try
        {
            await users.UpdateAsync(user, cancellationToken);
        }
        catch (ConcurrencyException)
        {
            // The user signed in again meanwhile; the repo data is saved, only the timestamp is skipped.
        }

        LogSynced(userId, seen.Count, added.Count, updated.Count, removedIds.Count);
        return new RepoSyncResultDto(seen.Count, added.Count, updated.Count, removedIds.Count, now);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Synced repos for user {UserId}: {Total} total, {Added} added, {Updated} updated, {Removed} removed")]
    private partial void LogSynced(Guid userId, int total, int added, int updated, int removed);
}

/// <summary>Deletes data that belonged to repositories the user can no longer access.</summary>
public sealed class RemovedRepoCleaner(
    IRepoIndexRepository indexes,
    ICodeChunkStore chunks,
    IConversationRepository conversations)
{
    public async Task CleanUpAsync(IReadOnlyCollection<Guid> removedRepoIds, CancellationToken cancellationToken)
    {
        if (removedRepoIds.Count == 0)
        {
            return;
        }

        // Summary jobs and their PDFs are kept as history; search data and chats are removed.
        await chunks.DeleteManyAsync(removedRepoIds, cancellationToken);
        await indexes.DeleteManyAsync(removedRepoIds, cancellationToken);
        await conversations.DeleteForReposAsync(removedRepoIds, cancellationToken);
    }
}
