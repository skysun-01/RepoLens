using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using RepoLens.Application.Auth;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Abstractions.Security;
using RepoLens.Application.Common.Errors;
using RepoLens.Domain.Auth;
using RepoLens.Domain.Users;
using RepoLens.Infrastructure.Security;

namespace RepoLens.UnitTests.Application;

public sealed class TokenServiceTests
{
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly InMemoryAuthCodes _codes = new();
    private readonly InMemoryRefreshTokens _refreshTokens = new();
    private readonly SecretTokenGenerator _secrets = new();
    private readonly User _user = User.Register(1, "octo", null, null, null, "encrypted", "repo", TestData.Now);
    private readonly TokenService _service;

    public TokenServiceTests()
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);

        var issuer = Substitute.For<IAccessTokenIssuer>();
        issuer.Issue(_user).Returns(new IssuedAccessToken("jwt", TestData.Now.AddHours(1)));

        _service = new TokenService(_codes, _refreshTokens, users, issuer, _secrets, TestData.Options(new AuthOptions()), _time, NullLogger<TokenService>.Instance);
    }

    [Fact]
    public async Task A_sign_in_code_works_exactly_once()
    {
        var code = await AddCodeAsync(TestData.Now.AddMinutes(1));

        var pair = await _service.ExchangeCodeAsync(code, CancellationToken.None);

        Assert.Equal("jwt", pair.AccessToken);
        Assert.False(string.IsNullOrEmpty(pair.RefreshToken));
        var error = await Assert.ThrowsAsync<UnauthorizedException>(() => _service.ExchangeCodeAsync(code, CancellationToken.None));
        Assert.Equal("invalid_code", error.Code);
    }

    [Fact]
    public async Task An_expired_sign_in_code_is_rejected()
    {
        var code = await AddCodeAsync(TestData.Now.AddSeconds(30));
        _time.Advance(TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.ExchangeCodeAsync(code, CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_rotates_the_token()
    {
        var first = await _service.ExchangeCodeAsync(await AddCodeAsync(TestData.Now.AddMinutes(1)), CancellationToken.None);

        var second = await _service.RefreshAsync(first.RefreshToken, CancellationToken.None);

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.False((await _refreshTokens.GetAsync(_secrets.Hash(first.RefreshToken), CancellationToken.None))!.IsActive(TestData.Now));
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_session()
    {
        var first = await _service.ExchangeCodeAsync(await AddCodeAsync(TestData.Now.AddMinutes(1)), CancellationToken.None);
        var second = await _service.RefreshAsync(first.RefreshToken, CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync(first.RefreshToken, CancellationToken.None));

        // The legitimate newer token is revoked too: whoever holds it must sign in again.
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync(second.RefreshToken, CancellationToken.None));
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var pair = await _service.ExchangeCodeAsync(await AddCodeAsync(TestData.Now.AddMinutes(1)), CancellationToken.None);

        await _service.RevokeAsync(pair.RefreshToken, CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync(pair.RefreshToken, CancellationToken.None));
    }

    private async Task<string> AddCodeAsync(DateTimeOffset expiresAt)
    {
        var code = _secrets.Generate();
        await _codes.AddAsync(new AuthCode(_secrets.Hash(code), _user.Id, TestData.Now, expiresAt), CancellationToken.None);
        return code;
    }

    private sealed class InMemoryAuthCodes : IAuthCodeRepository
    {
        private readonly Dictionary<string, AuthCode> _codes = [];

        public Task AddAsync(AuthCode code, CancellationToken cancellationToken)
        {
            _codes[code.Id] = code;
            return Task.CompletedTask;
        }

        public Task<AuthCode?> ConsumeAsync(string codeHash, CancellationToken cancellationToken) =>
            Task.FromResult(_codes.Remove(codeHash, out var code) ? code : null);
    }

    private sealed class InMemoryRefreshTokens : IRefreshTokenRepository
    {
        private readonly Dictionary<string, RefreshToken> _tokens = [];

        public Task AddAsync(RefreshToken token, CancellationToken cancellationToken)
        {
            _tokens[token.Id] = token;
            return Task.CompletedTask;
        }

        public Task<RefreshToken?> GetAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(_tokens.GetValueOrDefault(tokenHash));

        public Task<bool> TryRotateAsync(RefreshToken current, RefreshToken replacement, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (_tokens[current.Id].RevokedAt is not null)
            {
                return Task.FromResult(false);
            }

            _tokens[current.Id].Revoke(now, replacement.Id);
            _tokens[replacement.Id] = replacement;
            return Task.FromResult(true);
        }

        public Task RevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
        {
            _tokens.GetValueOrDefault(tokenHash)?.Revoke(now);
            return Task.CompletedTask;
        }

        public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            foreach (var token in _tokens.Values.Where(t => t.FamilyId == familyId))
            {
                token.Revoke(now);
            }

            return Task.CompletedTask;
        }
    }
}
