using System.Net;
using System.Net.Http.Json;
using RepoLens.Application.Auth;
using RepoLens.Application.Users;
using RepoLens.IntegrationTests.Infrastructure;

namespace RepoLens.IntegrationTests;

public sealed class AuthTests(RepoLensApiFactory factory) : IClassFixture<RepoLensApiFactory>
{
    [Fact]
    public async Task Signed_in_user_can_read_their_profile()
    {
        var client = await factory.CreateSignedInClientAsync("mona");

        var me = await client.GetFromJsonAsync<UserDto>("/api/v1/me", RepoLensApiFactory.Json);

        Assert.Equal("mona", me!.Login);
        Assert.Equal("mona@example.com", me.Email);
    }

    [Fact]
    public async Task Sign_in_code_can_be_exchanged_only_once()
    {
        var code = await factory.CreateSignInCodeAsync();
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/v1/auth/token", new { code });
        var second = await client.PostAsJsonAsync("/api/v1/auth/token", new { code });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal("invalid_code", await second.ProblemCodeAsync());
    }

    [Fact]
    public async Task Refresh_rotates_tokens_and_reuse_ends_the_session()
    {
        var client = factory.CreateClient();
        var tokens = await (await client.PostAsJsonAsync("/api/v1/auth/token", new { code = await factory.CreateSignInCodeAsync() })).ReadAsync<TokenPairDto>();

        var refreshed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var rotated = await refreshed.ReadAsync<TokenPairDto>();
        Assert.NotEqual(tokens.RefreshToken, rotated.RefreshToken);

        var reused = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);

        var afterReuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var client = factory.CreateClient();
        var tokens = await (await client.PostAsJsonAsync("/api/v1/auth/token", new { code = await factory.CreateSignInCodeAsync() })).ReadAsync<TokenPairDto>();

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = tokens.RefreshToken });
        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Development_callback_page_shows_the_tokens_for_swagger()
    {
        var code = await factory.CreateSignInCodeAsync();

        var html = await factory.CreateClient().GetStringAsync($"/api/v1/auth/dev/callback?code={Uri.EscapeDataString(code)}");

        Assert.Contains("Access token", html, StringComparison.Ordinal);
        Assert.Contains("Swagger UI", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tampered_access_token_is_rejected()
    {
        var client = await factory.CreateSignedInClientAsync();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token[..^4] + "AAAA");

        var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
