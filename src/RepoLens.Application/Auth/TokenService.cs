using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Abstractions.Security;
using RepoLens.Application.Common.Errors;
using RepoLens.Domain.Auth;
using RepoLens.Domain.Users;

namespace RepoLens.Application.Auth;

/// <summary>Exchanges one-time codes for tokens, rotates refresh tokens and revokes them on logout.</summary>
public sealed partial class TokenService(
    IAuthCodeRepository authCodes,
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    IAccessTokenIssuer accessTokenIssuer,
    ISecretTokenGenerator secretTokens,
    IOptions<AuthOptions> options,
    TimeProvider timeProvider,
    ILogger<TokenService> logger)
{
    public async Task<TokenPairDto> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw InvalidCode();
        }

        var authCode = await authCodes.ConsumeAsync(secretTokens.Hash(code), cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (authCode is null || authCode.IsExpired(now))
        {
            throw InvalidCode();
        }

        var user = await users.GetByIdAsync(authCode.UserId, cancellationToken) ?? throw InvalidCode();
        var (rawRefreshToken, refreshToken) = CreateRefreshToken(user.Id, Guid.NewGuid(), now);
        await refreshTokens.AddAsync(refreshToken, cancellationToken);

        return CreatePair(user, rawRefreshToken, refreshToken);
    }

    public async Task<TokenPairDto> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw InvalidRefreshToken();
        }

        var now = timeProvider.GetUtcNow();
        var current = await refreshTokens.GetAsync(secretTokens.Hash(refreshToken), cancellationToken) ?? throw InvalidRefreshToken();

        if (!current.IsActive(now))
        {
            if (current.RevokedAt is not null)
            {
                // A rotated token was presented again: it may have been stolen. End the whole session.
                LogRefreshTokenReuse(current.UserId, current.FamilyId);
                await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            }

            throw InvalidRefreshToken();
        }

        var user = await users.GetByIdAsync(current.UserId, cancellationToken) ?? throw InvalidRefreshToken();
        var (rawReplacement, replacement) = CreateRefreshToken(user.Id, current.FamilyId, now);

        if (!await refreshTokens.TryRotateAsync(current, replacement, now, cancellationToken))
        {
            // Two refreshes with the same token at once: treat it like reuse.
            LogRefreshTokenReuse(current.UserId, current.FamilyId);
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            throw InvalidRefreshToken();
        }

        return CreatePair(user, rawReplacement, replacement);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        await refreshTokens.RevokeAsync(secretTokens.Hash(refreshToken), timeProvider.GetUtcNow(), cancellationToken);
    }

    private (string Raw, RefreshToken Token) CreateRefreshToken(Guid userId, Guid familyId, DateTimeOffset now)
    {
        var raw = secretTokens.Generate();
        var token = new RefreshToken(secretTokens.Hash(raw), userId, familyId, now, now + options.Value.RefreshTokenLifetime);
        return (raw, token);
    }

    private TokenPairDto CreatePair(User user, string rawRefreshToken, RefreshToken refreshToken)
    {
        var accessToken = accessTokenIssuer.Issue(user);
        return new TokenPairDto(accessToken.Token, accessToken.ExpiresAt, rawRefreshToken, refreshToken.ExpiresAt);
    }

    private static UnauthorizedException InvalidCode() =>
        new("invalid_code", "The sign-in code is invalid, expired or already used. Sign in with GitHub again.");

    private static UnauthorizedException InvalidRefreshToken() =>
        new("invalid_refresh_token", "The refresh token is invalid or expired. Sign in with GitHub again.");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reuse detected for user {UserId}; revoked token family {FamilyId}")]
    private partial void LogRefreshTokenReuse(Guid userId, Guid familyId);
}
