using System.Security.Cryptography;
using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Abstractions.Security;
using RepoLens.Domain.Users;

namespace RepoLens.Application.Users;

/// <summary>Loads a user and decrypts their GitHub token for calls made on their behalf.</summary>
public sealed class GitHubTokenAccessor(IUserRepository users, ITokenProtector tokenProtector)
{
    public async Task<(User User, string AccessToken)> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken) ?? throw UserService.UserMissing();

        try
        {
            return (user, tokenProtector.Unprotect(user.EncryptedAccessToken));
        }
        catch (CryptographicException)
        {
            // The encryption key is gone (for example, rotated away). A fresh sign-in stores a new token.
            throw new GitHubAuthorizationException();
        }
    }
}
