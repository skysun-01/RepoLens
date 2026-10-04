using System.Security.Claims;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RepoLens.Infrastructure.GitHub;
using RepoLens.Infrastructure.Security;

namespace RepoLens.Api.Auth;

/// <summary>
/// Two ways in. API calls carry a JWT bearer token. Signing in uses GitHub OAuth, whose result lives
/// in a short-lived cookie only long enough to be exchanged for a one-time code.
/// </summary>
public static class AuthenticationSetup
{
    /// <summary>Holds the GitHub identity for the few seconds between the OAuth callback and issuing the one-time code.</summary>
    public const string ExternalScheme = "GitHubExternal";

    public const string CallbackPath = "/api/v1/auth/github/callback";

    /// <summary>
    /// Stand-in credentials when GitHub:ClientId/ClientSecret are not set. The OAuth handler validates its
    /// options on every request, so empty values would break every endpoint; with placeholders the API
    /// runs normally and only sign-in reports that it is not configured.
    /// </summary>
    public const string NotConfigured = "not-configured";
    public const string ScopesClaim = "urn:github:scopes";
    public const string AvatarClaim = "urn:github:avatar";
    public const string NameClaim = "urn:github:name";

    public static IServiceCollection AddRepoLensAuthentication(this IServiceCollection services)
    {
        services.AddOptions<FrontendOptions>().BindConfiguration(FrontendOptions.SectionName);

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer()
            .AddCookie(ExternalScheme, options =>
            {
                options.Cookie.Name = "repolens.github-signin";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                options.SlidingExpiration = false;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
            })
            .AddGitHub();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwt) =>
            {
                var settings = jwt.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(settings.SigningKeyBytes()),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                };
            });

        services.AddOptions<GitHubAuthenticationOptions>(GitHubAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<GitHubOptions>, IOptions<FrontendOptions>>((options, gitHub, frontend) =>
            {
                options.SignInScheme = ExternalScheme;
                options.ClientId = string.IsNullOrWhiteSpace(gitHub.Value.ClientId) ? NotConfigured : gitHub.Value.ClientId;
                options.ClientSecret = string.IsNullOrWhiteSpace(gitHub.Value.ClientSecret) ? NotConfigured : gitHub.Value.ClientSecret;
                options.CallbackPath = CallbackPath;
                options.SaveTokens = true;
                options.UsePkce = true;

                options.Scope.Clear();
                foreach (var scope in gitHub.Value.Scopes)
                {
                    options.Scope.Add(scope);
                }

                // The provider already maps id, login, name (urn:github:name) and email; add the avatar.
                options.ClaimActions.MapJsonKey(AvatarClaim, "avatar_url");

                options.Events.OnCreatingTicket = context =>
                {
                    // GitHub reports the scopes it actually granted in the token response.
                    if (context.TokenResponse.Response?.RootElement.TryGetProperty("scope", out var granted) == true
                        && granted.GetString() is { } scopes)
                    {
                        context.Identity?.AddClaim(new Claim(ScopesClaim, scopes));
                    }

                    return Task.CompletedTask;
                };

                options.Events.OnRemoteFailure = context =>
                {
                    // For example the user clicked "Cancel" on GitHub: send them back to the frontend with an error.
                    var error = context.Request.Query["error"].FirstOrDefault() ?? "github_signin_failed";
                    context.Response.Redirect(frontend.Value.BuildCallbackUrl("error", error));
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();
        return services;
    }
}
