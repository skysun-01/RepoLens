namespace RepoLens.Domain.Auth;

/// <summary>
/// A long-lived token that gets a new access token. Rotated on every use: the old token is revoked
/// and points at its replacement. Tokens issued from one sign-in share a <see cref="FamilyId"/>, so
/// reuse of a revoked token can revoke the whole family.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public RefreshToken(string tokenHash, Guid userId, Guid familyId, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        Id = tokenHash;
        UserId = userId;
        FamilyId = familyId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>SHA-256 hash of the token.</summary>
    public string Id { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? ReplacedByHash { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset now, string? replacedByHash = null)
    {
        RevokedAt ??= now;
        ReplacedByHash ??= replacedByHash;
    }
}
