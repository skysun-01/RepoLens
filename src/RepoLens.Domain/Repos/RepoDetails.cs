namespace RepoLens.Domain.Repos;

/// <summary>Repository metadata as reported by GitHub.</summary>
public sealed record RepoDetails(
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
    long SizeKb,
    DateTimeOffset? PushedAt);
