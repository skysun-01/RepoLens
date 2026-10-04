using System.Net;
using RepoLens.IntegrationTests.Infrastructure;

namespace RepoLens.IntegrationTests;

public sealed class HostingTests(RepoLensApiFactory factory) : IClassFixture<RepoLensApiFactory>
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_report_healthy(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_ui_is_served_in_development()
    {
        var response = await factory.CreateClient().GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("swagger", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenApi_document_describes_the_endpoints_and_the_bearer_scheme()
    {
        var document = await factory.CreateClient().GetStringAsync("/openapi/v1.json");

        Assert.Contains("\"Bearer\"", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/repos", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/repos/{repoId}/summaries", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/summaries/{jobId}/pdf", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/repos/{repoId}/questions/stream", document, StringComparison.Ordinal);
        Assert.Contains("Summarizes a repository into an onboarding PDF", document, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/v1/repos")]
    [InlineData("/api/v1/me")]
    [InlineData("/api/v1/summaries/7d3f4b3e-0000-0000-0000-000000000000")]
    public async Task Api_requires_a_bearer_token(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Github_login_redirects_to_github_with_pkce_and_the_callback()
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/v1/auth/github/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://github.com/login/oauth/authorize", location, StringComparison.Ordinal);
        Assert.Contains("client_id=test-client-id", location, StringComparison.Ordinal);
        Assert.Contains("code_challenge=", location, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("/api/v1/auth/github/callback"), location, StringComparison.Ordinal);
        Assert.Contains("repo", location, StringComparison.Ordinal);
    }
}
