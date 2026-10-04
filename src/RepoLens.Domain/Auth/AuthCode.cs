namespace RepoLens.Domain.Auth;

/// <summary>
/// One-time code handed to the frontend after GitHub sign-in, exchanged for tokens.
/// Only its hash is stored; it expires within a minute and can be used once.
/// </summary>
public sealed class AuthCode
{
    private AuthCode()
    {
    }

    public AuthCode(string codeHash, Guid userId, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        Id = codeHash;
        UserId = userId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>SHA-256 hash of the code.</summary>
    public string Id { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
