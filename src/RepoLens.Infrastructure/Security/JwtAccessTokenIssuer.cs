using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RepoLens.Application.Common.Abstractions.Security;
using RepoLens.Domain.Users;

namespace RepoLens.Infrastructure.Security;

/// <summary>Issues HMAC-signed JWT access tokens. The subject claim is the RepoLens user id.</summary>
internal sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider) : IAccessTokenIssuer
{
    public const string LoginClaim = "login";
    public const string GitHubIdClaim = "gh_id";

    private readonly JsonWebTokenHandler _handler = new();

    public IssuedAccessToken Issue(User user)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var expires = now + settings.AccessTokenLifetime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
                new Claim(LoginClaim, user.Login),
                new Claim(GitHubIdClaim, user.GitHubId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(settings.SigningKeyBytes()), SecurityAlgorithms.HmacSha256),
        };

        return new IssuedAccessToken(_handler.CreateToken(descriptor), expires);
    }
}
