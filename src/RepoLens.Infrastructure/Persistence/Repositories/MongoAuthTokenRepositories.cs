using MongoDB.Driver;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Domain.Auth;

namespace RepoLens.Infrastructure.Persistence.Repositories;

internal sealed class MongoAuthCodeRepository(MongoContext context) : IAuthCodeRepository
{
    public Task AddAsync(AuthCode code, CancellationToken cancellationToken) =>
        context.AuthCodes.InsertOneAsync(code, cancellationToken: cancellationToken);

    public async Task<AuthCode?> ConsumeAsync(string codeHash, CancellationToken cancellationToken) =>
        await context.AuthCodes.FindOneAndDeleteAsync(c => c.Id == codeHash, cancellationToken: cancellationToken);
}

internal sealed class MongoRefreshTokenRepository(MongoContext context) : IRefreshTokenRepository
{
    private static FilterDefinitionBuilder<RefreshToken> Filter => Builders<RefreshToken>.Filter;

    private IMongoCollection<RefreshToken> Tokens => context.RefreshTokens;

    public Task AddAsync(RefreshToken token, CancellationToken cancellationToken) =>
        Tokens.InsertOneAsync(token, cancellationToken: cancellationToken);

    public async Task<RefreshToken?> GetAsync(string tokenHash, CancellationToken cancellationToken) =>
        await Tokens.Find(t => t.Id == tokenHash).FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryRotateAsync(RefreshToken current, RefreshToken replacement, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Only one request can flip RevokedAt from null; a concurrent second refresh gets null back.
        var revoked = await Tokens.FindOneAndUpdateAsync(
            Filter.Eq(t => t.Id, current.Id) & Filter.Eq(t => t.RevokedAt, null),
            Builders<RefreshToken>.Update.Set(t => t.RevokedAt, now).Set(t => t.ReplacedByHash, replacement.Id),
            cancellationToken: cancellationToken);

        if (revoked is null)
        {
            return false;
        }

        current.Revoke(now, replacement.Id);
        await Tokens.InsertOneAsync(replacement, cancellationToken: cancellationToken);
        return true;
    }

    public Task RevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken) =>
        Tokens.UpdateOneAsync(
            Filter.Eq(t => t.Id, tokenHash) & Filter.Eq(t => t.RevokedAt, null),
            Builders<RefreshToken>.Update.Set(t => t.RevokedAt, now),
            cancellationToken: cancellationToken);

    public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Tokens.UpdateManyAsync(
            Filter.Eq(t => t.FamilyId, familyId) & Filter.Eq(t => t.RevokedAt, null),
            Builders<RefreshToken>.Update.Set(t => t.RevokedAt, now),
            cancellationToken: cancellationToken);
}
