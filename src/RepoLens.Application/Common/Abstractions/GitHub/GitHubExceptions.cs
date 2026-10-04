using RepoLens.Application.Common.Errors;

namespace RepoLens.Application.Common.Abstractions.GitHub;

/// <summary>GitHub rejected the stored token (revoked, expired or missing scope). The user must sign in again.</summary>
public sealed class GitHubAuthorizationException()
    : AppException("github_reauth_required", "GitHub rejected the saved access token. Sign in with GitHub again.", ErrorKind.Unauthorized);

public sealed class GitHubNotFoundException(string what)
    : AppException("github_not_found", $"GitHub could not find {what}, or this account no longer has access to it.", ErrorKind.NotFound);

public sealed class GitHubRateLimitException(DateTimeOffset? resetAt)
    : AppException("github_rate_limited", "GitHub's API rate limit for this account is used up. Try again later.", ErrorKind.RateLimited)
{
    public DateTimeOffset? ResetAt { get; } = resetAt;
}

public sealed class GitHubUnavailableException(string message, Exception? innerException = null)
    : AppException("github_unavailable", message, ErrorKind.Unavailable, innerException);

/// <summary>The repository or branch has no commits, so there is nothing to summarize.</summary>
public sealed class RepositoryEmptyException()
    : AppException("repo_empty", "This repository has no commits yet, so there is nothing to summarize.", ErrorKind.Conflict);

/// <summary>The repository archive exceeds the configured size limit.</summary>
public sealed class RepositoryTooLargeException(long limitBytes)
    : AppException("repo_too_large", $"The repository archive is larger than the {limitBytes / (1024 * 1024)} MB limit.", ErrorKind.Validation);
