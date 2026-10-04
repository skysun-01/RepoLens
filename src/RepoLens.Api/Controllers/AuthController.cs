using System.Globalization;
using System.Net;
using System.Security.Claims;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RepoLens.Api.Auth;
using RepoLens.Api.Contracts;
using RepoLens.Api.RateLimiting;
using RepoLens.Application.Auth;
using RepoLens.Application.Common.Errors;
using RepoLens.Infrastructure.GitHub;

namespace RepoLens.Api.Controllers;

public sealed class GitHubSignInNotConfiguredException()
    : AppException(
        "github_signin_not_configured",
        "GitHub sign-in is not configured. Create a GitHub OAuth App and set GitHub:ClientId and GitHub:ClientSecret (see README).",
        ErrorKind.Unavailable);

/// <summary>GitHub sign-in and token management.</summary>
[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingSetup.SignIn)]
[Produces("application/json")]
public sealed class AuthController(
    SignInService signIn,
    TokenService tokens,
    IOptions<FrontendOptions> frontend,
    IOptions<GitHubOptions> gitHub,
    IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>Starts GitHub sign-in.</summary>
    /// <remarks>
    /// Open this URL in the browser (a full page navigation, not fetch/XHR). After GitHub sign-in the browser
    /// is redirected to the frontend's callback URL with <c>?code=…</c>; exchange it with <c>POST /api/v1/auth/token</c>.
    /// Returns 503 <c>github_signin_not_configured</c> until GitHub:ClientId and GitHub:ClientSecret are set.
    /// </remarks>
    [HttpGet("github/login")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult Login()
    {
        if (string.IsNullOrWhiteSpace(gitHub.Value.ClientId) || string.IsNullOrWhiteSpace(gitHub.Value.ClientSecret))
        {
            throw new GitHubSignInNotConfiguredException();
        }

        return Challenge(
            new AuthenticationProperties { RedirectUri = Url.Action(nameof(CompleteGitHubSignIn)) },
            GitHubAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>Last step of the GitHub redirect chain. Browsers arrive here automatically.</summary>
    [HttpGet("github/complete")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> CompleteGitHubSignIn(CancellationToken cancellationToken)
    {
        var result = await HttpContext.AuthenticateAsync(AuthenticationSetup.ExternalScheme);
        await HttpContext.SignOutAsync(AuthenticationSetup.ExternalScheme);

        var principal = result.Principal;
        var accessToken = result.Properties?.GetTokenValue("access_token");

        if (!result.Succeeded
            || principal is null
            || string.IsNullOrEmpty(accessToken)
            || !long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.Integer, CultureInfo.InvariantCulture, out var gitHubId))
        {
            return Redirect(frontend.Value.BuildCallbackUrl("error", "github_signin_failed"));
        }

        var identity = new GitHubIdentity(
            gitHubId,
            principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            principal.FindFirstValue(AuthenticationSetup.NameClaim),
            principal.FindFirstValue(ClaimTypes.Email),
            principal.FindFirstValue(AuthenticationSetup.AvatarClaim));

        var code = await signIn.CompleteGitHubSignInAsync(
            identity,
            accessToken,
            principal.FindFirstValue(AuthenticationSetup.ScopesClaim) ?? string.Empty,
            cancellationToken);

        return Redirect(frontend.Value.BuildCallbackUrl("code", code));
    }

    /// <summary>Exchanges the one-time sign-in code for an access token and a refresh token.</summary>
    /// <remarks>The code is valid for 60 seconds and can be used once.</remarks>
    [HttpPost("token")]
    [ProducesResponseType<TokenPairDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<TokenPairDto> ExchangeCode(ExchangeCodeRequest request, CancellationToken cancellationToken) =>
        tokens.ExchangeCodeAsync(request.Code, cancellationToken);

    /// <summary>Gets a new access token. The refresh token rotates: use the new one next time.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType<TokenPairDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<TokenPairDto> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken) =>
        tokens.RefreshAsync(request.RefreshToken, cancellationToken);

    /// <summary>Signs out by revoking the refresh token.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await tokens.RevokeAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Development only: stands in for the frontend's callback page. Exchanges the code and shows the
    /// tokens so they can be pasted into Swagger UI.
    /// </summary>
    [HttpGet("dev/callback")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> DevelopmentCallback([FromQuery] string? code, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            return Content(DevPage("Sign-in failed", $"<p>GitHub sign-in did not complete: <code>{WebUtility.HtmlEncode(error ?? "no code")}</code></p><p><a href=\"/api/v1/auth/github/login\">Try again</a></p>"), "text/html");
        }

        TokenPairDto pair;
        try
        {
            pair = await tokens.ExchangeCodeAsync(code, cancellationToken);
        }
        catch (UnauthorizedException ex)
        {
            return Content(DevPage("Sign-in failed", $"<p>{WebUtility.HtmlEncode(ex.Message)}</p><p><a href=\"/api/v1/auth/github/login\">Try again</a></p>"), "text/html");
        }

        var body = $"""
            <p>You are signed in. In <a href="/swagger">Swagger UI</a>, click <b>Authorize</b> and paste the access token.</p>
            <h2>Access token <small>(expires {pair.AccessTokenExpiresAt:u})</small></h2>
            <textarea readonly rows="6" onclick="this.select()">{WebUtility.HtmlEncode(pair.AccessToken)}</textarea>
            <h2>Refresh token <small>(for POST /api/v1/auth/refresh)</small></h2>
            <textarea readonly rows="2" onclick="this.select()">{WebUtility.HtmlEncode(pair.RefreshToken)}</textarea>
            <p><a href="/swagger">Open Swagger UI →</a></p>
            """;
        return Content(DevPage("RepoLens: signed in", body), "text/html");
    }

    private static string DevPage(string title, string body) => $$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
          body { font-family: system-ui, sans-serif; max-width: 760px; margin: 40px auto; padding: 0 16px; color: #17202b; }
          textarea { width: 100%; font-family: Consolas, monospace; font-size: 12px; }
          h2 { font-size: 15px; margin-top: 24px; } small { color: #5a6472; font-weight: normal; }
        </style></head>
        <body><h1>{{WebUtility.HtmlEncode(title)}}</h1>{{body}}</body></html>
        """;
}
