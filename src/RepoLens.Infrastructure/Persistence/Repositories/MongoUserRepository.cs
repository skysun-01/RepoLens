using MongoDB.Driver;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Domain.Users;

namespace RepoLens.Infrastructure.Persistence.Repositories;

internal sealed class MongoUserRepository(MongoContext context) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.Users.Find(u => u.Id == userId).FirstOrDefaultAsync(cancellationToken);

    public async Task<User?> GetByGitHubIdAsync(long gitHubId, CancellationToken cancellationToken) =>
        await context.Users.Find(u => u.GitHubId == gitHubId).FirstOrDefaultAsync(cancellationToken);

    public Task AddAsync(User user, CancellationToken cancellationToken) =>
        MongoWrites.InsertAsync(context.Users, user, cancellationToken);

    public Task UpdateAsync(User user, CancellationToken cancellationToken) =>
        MongoWrites.ReplaceAsync(context.Users, user, cancellationToken);
}
