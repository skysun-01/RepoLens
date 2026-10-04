namespace RepoLens.Api.Auth;

public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    /// <summary>
    /// Where the API sends the browser after GitHub sign-in, with <c>?code=…</c> (or <c>?error=…</c>) appended.
    /// Absolute for a real frontend (for example http://localhost:3000/auth/callback). In Development it can
    /// point at the API's own <c>/api/v1/auth/dev/callback</c>, which exchanges the code and shows the tokens.
    /// </summary>
    public string AuthCallbackUrl { get; set; } = "/api/v1/auth/dev/callback";

    /// <summary>Origins allowed to call the API from a browser (CORS).</summary>
    public string[] AllowedOrigins { get; set; } = [];

    public string BuildCallbackUrl(string key, string value)
    {
        var separator = AuthCallbackUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{AuthCallbackUrl}{separator}{key}={Uri.EscapeDataString(value)}";
    }
}
