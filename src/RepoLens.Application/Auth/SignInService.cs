using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Abstractions.Security;
using RepoLens.Application.Common.Errors;
using RepoLens.Domain.Auth;
using RepoLens.Domain.Users;

namespace RepoLens.Application.Auth;

/// <summary>Finishes a GitHub sign-in: stores the user and their encrypted token, and issues a one-time code.</summary>
public sealed partial class SignInService(
    IUserRepository users,
    IAuthCodeRepository authCodes,
    ITokenProtector tokenProtector,
    ISecretTokenGenerator secretTokens,
    IOptions<AuthOptions> options,
    TimeProvider timeProvider,
    ILogger<SignInService> logger)
{
    /// <returns>The one-time code the frontend exchanges for tokens.</returns>
    public async Task<string> CompleteGitHubSignInAsync(
        GitHubIdentity identity,
        string gitHubAccessToken,
        string grantedScopes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (identity.GitHubId <= 0 || string.IsNullOrWhiteSpace(identity.Login))
        {
            throw new UnauthorizedException("github_profile_invalid", "GitHub did not return a usable profile.");
        }

        if (string.IsNullOrWhiteSpace(gitHubAccessToken))
        {
            throw new UnauthorizedException("github_token_missing", "GitHub did not return an access token.");
        }

        var user = await UpsertUserAsync(identity, tokenProtector.Protect(gitHubAccessToken), grantedScopes, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var code = secretTokens.Generate();
        await authCodes.AddAsync(
            new AuthCode(secretTokens.Hash(code), user.Id, now, now + options.Value.AuthCodeLifetime),
            cancellationToken);

        LogSignedIn(user.Id, user.Login);
        return code;
    }

    private async Task<User> UpsertUserAsync(GitHubIdentity identity, string encryptedToken, string scopes, CancellationToken cancellationToken)
    {
        // Two sign-ins of a brand-new user can race; the unique GitHub id index makes one of them
        // lose, and the retry then takes the update path.
        for (var attempt = 1; ; attempt++)
        {
            var now = timeProvider.GetUtcNow();
            var user = await users.GetByGitHubIdAsync(identity.GitHubId, cancellationToken);

            try
            {
                if (user is null)
                {
                    user = User.Register(identity.GitHubId, identity.Login, identity.Name, identity.Email, identity.AvatarUrl, encryptedToken, scopes, now);
                    await users.AddAsync(user, cancellationToken);
                }
                else
                {
                    user.RecordSignIn(identity.Login, identity.Name, identity.Email, identity.AvatarUrl, encryptedToken, scopes, now);
                    await users.UpdateAsync(user, cancellationToken);
                }

                return user;
            }
            catch (ConcurrencyException) when (attempt < 3)
            {
                // Reload and try again.
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} ({Login}) signed in with GitHub")]
    private partial void LogSignedIn(Guid userId, string login);
}
