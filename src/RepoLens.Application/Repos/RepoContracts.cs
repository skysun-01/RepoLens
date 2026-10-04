using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Repos;

/// <summary>Query string of <c>GET /repos</c>.</summary>
public sealed record RepoListQuery(int? Page, int? PageSize, string? Search, string? Sort);

/// <summary>One row of the repository table.</summary>
public sealed record RepoDto(
    Guid Id,
    long GitHubRepoId,
    string Owner,
    string Name,
    string FullName,
    string? Description,
    string? Language,
    bool IsPrivate,
    bool IsFork,
    bool IsArchived,
    string DefaultBranch,
    string HtmlUrl,
    int Stars,
    DateTimeOffset? PushedAt,
    RepoSummaryStateDto Summary,
    RepoIndexStateDto? Index);

/// <summary>
/// Summary state for the table: the latest job (to show progress or an error) and the latest
/// finished PDF, which stays downloadable while a newer summary is being generated.
/// </summary>
public sealed record RepoSummaryStateDto(
    Guid? LatestJobId,
    SummaryStatus? Status,
    SummaryStage? Stage,
    int? Progress,
    string? ErrorCode,
    string? ErrorMessage,
    LatestPdfDto? LatestPdf);

public sealed record LatestPdfDto(Guid JobId, string CommitSha, DateTimeOffset GeneratedAt, string Url);

public sealed record RepoIndexStateDto(string CommitSha, int ChunkCount, int FileCount, DateTimeOffset IndexedAt);

public sealed record RepoSyncResultDto(int Total, int Added, int Updated, int Removed, DateTimeOffset SyncedAt);
