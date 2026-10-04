using Microsoft.Extensions.Options;
using RepoLens.Application.Common.Abstractions;
using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Errors;
using RepoLens.Application.Common.Models;
using RepoLens.Application.Users;
using RepoLens.Domain.Repos;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Repos;

/// <summary>Reads the repository table, joining each row with its summary and index state.</summary>
public sealed class RepoQueryService(
    IRepoRepository repos,
    IRepoIndexRepository indexes,
    ISummaryJobRepository jobs,
    IUserRepository users,
    RepoSyncService syncService,
    IResourceLinks links,
    IOptions<RepoOptions> options,
    TimeProvider timeProvider)
{
    private const int MaxSearchLength = 100;

    public async Task<PagedResult<RepoDto>> ListAsync(Guid userId, RepoListQuery query, CancellationToken cancellationToken)
    {
        var search = Validate(query);
        await SyncIfStaleAsync(userId, cancellationToken);

        var page = await repos.SearchAsync(userId, search, cancellationToken);
        var rows = await ToDtosAsync(page.Items, cancellationToken);
        return new PagedResult<RepoDto>(rows, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<RepoDto> GetAsync(Guid userId, Guid repoId, CancellationToken cancellationToken)
    {
        var repo = await repos.GetAsync(userId, repoId, cancellationToken) ?? throw new NotFoundException("Repository", repoId);
        return (await ToDtosAsync([repo], cancellationToken))[0];
    }

    private async Task SyncIfStaleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken) ?? throw UserService.UserMissing();
        var autoSyncAfter = options.Value.AutoSyncAfter;
        var isStale = user.ReposSyncedAt is null
            || (autoSyncAfter > TimeSpan.Zero && user.ReposSyncedAt < timeProvider.GetUtcNow() - autoSyncAfter);

        if (!isStale)
        {
            return;
        }

        try
        {
            await syncService.SyncAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (user.ReposSyncedAt is not null && ex is GitHubRateLimitException or GitHubUnavailableException)
        {
            // An automatic refresh is best effort: show the last synced list rather than an error.
            // POST /repos/sync still reports the GitHub problem to a user who asks for a refresh explicitly.
        }
    }

    private async Task<IReadOnlyList<RepoDto>> ToDtosAsync(IReadOnlyList<Repo> page, CancellationToken cancellationToken)
    {
        if (page.Count == 0)
        {
            return [];
        }

        var ids = page.Select(r => r.Id).ToList();
        var latestJobs = await jobs.GetLatestPerRepoAsync(ids, completedOnly: false, cancellationToken);
        var latestCompleted = await jobs.GetLatestPerRepoAsync(ids, completedOnly: true, cancellationToken);
        var repoIndexes = await indexes.GetManyAsync(ids, cancellationToken);

        return page.Select(repo => ToDto(
                repo,
                latestJobs.GetValueOrDefault(repo.Id),
                latestCompleted.GetValueOrDefault(repo.Id),
                repoIndexes.GetValueOrDefault(repo.Id)))
            .ToList();
    }

    private RepoDto ToDto(Repo repo, SummaryJob? latest, SummaryJob? completed, RepoIndex? index)
    {
        var latestPdf = completed is { Pdf: not null, CommitSha: not null, CompletedAt: not null }
            ? new LatestPdfDto(completed.Id, completed.CommitSha, completed.CompletedAt.Value, links.SummaryPdf(completed.Id))
            : null;

        var summary = new RepoSummaryStateDto(
            latest?.Id,
            latest?.Status,
            latest?.Stage,
            latest?.Progress,
            latest?.Error?.Code,
            latest?.Error?.Message,
            latestPdf);

        var indexState = index is null ? null : new RepoIndexStateDto(index.CommitSha, index.ChunkCount, index.FileCount, index.IndexedAt);

        return new RepoDto(
            repo.Id, repo.GitHubRepoId, repo.Owner, repo.Name, repo.FullName, repo.Description, repo.Language,
            repo.IsPrivate, repo.IsFork, repo.IsArchived, repo.DefaultBranch, repo.HtmlUrl, repo.Stars, repo.PushedAt,
            summary, indexState);
    }

    private RepoSearch Validate(RepoListQuery query)
    {
        var errors = new Dictionary<string, string[]>();
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? options.Value.DefaultPageSize;

        if (page < 1)
        {
            errors["page"] = ["Page must be 1 or greater."];
        }

        if (pageSize < 1 || pageSize > options.Value.MaxPageSize)
        {
            errors["pageSize"] = [$"Page size must be between 1 and {options.Value.MaxPageSize}."];
        }

        var text = query.Search?.Trim();
        if (text?.Length > MaxSearchLength)
        {
            errors["search"] = [$"Search text must be {MaxSearchLength} characters or fewer."];
        }

        RepoSort sort = RepoSort.RecentlyPushed;
        if (!string.IsNullOrWhiteSpace(query.Sort) && !TryParseSort(query.Sort, out sort))
        {
            errors["sort"] = ["Sort must be one of: pushed, name, stars."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException("The repository query is invalid.", errors);
        }

        return new RepoSearch(string.IsNullOrEmpty(text) ? null : text, sort, page, pageSize);
    }

    private static bool TryParseSort(string value, out RepoSort sort)
    {
        sort = value.Trim().ToLowerInvariant() switch
        {
            "pushed" or "updated" or "recent" => RepoSort.RecentlyPushed,
            "name" => RepoSort.Name,
            "stars" => RepoSort.Stars,
            _ => (RepoSort)(-1),
        };
        return Enum.IsDefined(sort);
    }
}
