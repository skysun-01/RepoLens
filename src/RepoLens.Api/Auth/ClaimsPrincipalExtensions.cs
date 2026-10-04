using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using RepoLens.Application.Common.Errors;

namespace RepoLens.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The RepoLens user id from the JWT subject claim.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
            ? userId
            : throw new UnauthorizedException("invalid_token", "The access token does not identify a user.");
}
