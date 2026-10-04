using RepoLens.Domain.Users;

namespace RepoLens.Application.Common.Abstractions.Persistence;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<User?> GetByGitHubIdAsync(long gitHubId, CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);

    /// <summary>Saves changes; throws <see cref="Errors.ConcurrencyException"/> if someone else saved first.</summary>
    Task UpdateAsync(User user, CancellationToken cancellationToken);
}
