namespace RepoLens.Application.Auth;

/// <summary>The GitHub account that just signed in, as reported by GitHub's OAuth flow.</summary>
public sealed record GitHubIdentity(long GitHubId, string Login, string? Name, string? Email, string? AvatarUrl);

public sealed record TokenPairDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt)
{
    public string TokenType => "Bearer";
}
