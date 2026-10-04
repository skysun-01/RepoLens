using RepoLens.Domain.Common;

namespace RepoLens.Domain.Users;

/// <summary>A person who signed in with GitHub. Holds their encrypted GitHub access token.</summary>
public sealed class User : Entity
{
    private User()
    {
    }

    private User(Guid id, long gitHubId, DateTimeOffset now)
        : base(id, now)
    {
        GitHubId = gitHubId;
        LastLoginAt = now;
    }

    public long GitHubId { get; private set; }

    public string Login { get; private set; } = string.Empty;

    public string? Name { get; private set; }

    public string? Email { get; private set; }

    public string? AvatarUrl { get; private set; }

    /// <summary>GitHub OAuth token, encrypted at rest. Never returned by the API.</summary>
    public string EncryptedAccessToken { get; private set; } = string.Empty;

    /// <summary>Scopes GitHub actually granted, comma separated.</summary>
    public string Scopes { get; private set; } = string.Empty;

    public DateTimeOffset LastLoginAt { get; private set; }

    public DateTimeOffset? ReposSyncedAt { get; private set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Login : Name;

    public static User Register(
        long gitHubId,
        string login,
        string? name,
        string? email,
        string? avatarUrl,
        string encryptedAccessToken,
        string scopes,
        DateTimeOffset now)
    {
        var user = new User(Guid.CreateVersion7(now), gitHubId, now);
        user.RecordSignIn(login, name, email, avatarUrl, encryptedAccessToken, scopes, now);
        return user;
    }

    /// <summary>Refreshes the profile and token each time the user signs in again.</summary>
    public void RecordSignIn(
        string login,
        string? name,
        string? email,
        string? avatarUrl,
        string encryptedAccessToken,
        string scopes,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            throw new DomainException("GitHub login is required.");
        }

        if (string.IsNullOrWhiteSpace(encryptedAccessToken))
        {
            throw new DomainException("An access token is required to sign in.");
        }

        Login = login;
        Name = name;
        Email = email;
        AvatarUrl = avatarUrl;
        EncryptedAccessToken = encryptedAccessToken;
        Scopes = scopes;
        LastLoginAt = now;
        Touch(now);
    }

    public void MarkReposSynced(DateTimeOffset now)
    {
        ReposSyncedAt = now;
        Touch(now);
    }
}
