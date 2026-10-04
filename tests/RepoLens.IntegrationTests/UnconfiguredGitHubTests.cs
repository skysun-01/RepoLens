using System.Net;
using RepoLens.IntegrationTests.Infrastructure;

namespace RepoLens.IntegrationTests;

/// <summary>A fresh checkout has no GitHub OAuth App yet: the API must still start and explain what is missing.</summary>
public sealed class UnconfiguredGitHubTests(UnconfiguredGitHubTests.Factory factory) : IClassFixture<UnconfiguredGitHubTests.Factory>
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/swagger/index.html")]
    [InlineData("/openapi/v1.json")]
    public async Task Api_works_without_github_credentials(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Sign_in_explains_that_github_is_not_configured()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/auth/github/login");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("github_signin_not_configured", await response.ProblemCodeAsync());
    }

    public sealed class Factory(MongoFixture mongo) : RepoLensApiFactory(mongo)
    {
        protected override void AdjustSettings(Dictionary<string, string?> settings)
        {
            settings["GitHub:ClientId"] = string.Empty;
            settings["GitHub:ClientSecret"] = string.Empty;
        }
    }
}
