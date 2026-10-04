using RepoLens.Domain.Auth;

namespace RepoLens.Application.Common.Abstractions.Persistence;

public interface IAuthCodeRepository
{
    Task AddAsync(AuthCode code, CancellationToken cancellationToken);

    /// <summary>Atomically finds and deletes the code, so it can be used only once.</summary>
    Task<AuthCode?> ConsumeAsync(string codeHash, CancellationToken cancellationToken);
}

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);

    Task<RefreshToken?> GetAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically revokes <paramref name="current"/> and stores <paramref name="replacement"/>.
    /// Returns false if <paramref name="current"/> was already revoked by a concurrent request.
    /// </summary>
    Task<bool> TryRotateAsync(RefreshToken current, RefreshToken replacement, DateTimeOffset now, CancellationToken cancellationToken);

    Task RevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken);

    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken);
}
