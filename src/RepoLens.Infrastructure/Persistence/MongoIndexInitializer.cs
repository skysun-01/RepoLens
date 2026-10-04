using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using RepoLens.Domain.Auth;
using RepoLens.Domain.Conversations;
using RepoLens.Domain.Indexing;
using RepoLens.Domain.Repos;
using RepoLens.Domain.Summaries;
using RepoLens.Domain.Users;

namespace RepoLens.Infrastructure.Persistence;

/// <summary>
/// Creates the indexes RepoLens relies on at startup. Creating an index that already exists is a
/// no-op, so every API and Worker instance can run this safely.
/// </summary>
internal sealed partial class MongoIndexInitializer(MongoContext context, ILogger<MongoIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await context.Users.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(u => u.GitHubId), new CreateIndexOptions { Unique = true, Name = "ux_github_id" }),
        ], cancellationToken);

        await context.Repos.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Repo>(
                Builders<Repo>.IndexKeys.Ascending(r => r.UserId).Ascending(r => r.GitHubRepoId),
                new CreateIndexOptions { Unique = true, Name = "ux_user_github_repo" }),
            new CreateIndexModel<Repo>(Builders<Repo>.IndexKeys.Ascending(r => r.UserId).Descending(r => r.PushedAt), new CreateIndexOptions { Name = "ix_user_pushed" }),
            new CreateIndexModel<Repo>(Builders<Repo>.IndexKeys.Ascending(r => r.UserId).Ascending(r => r.Name), new CreateIndexOptions { Name = "ix_user_name" }),
        ], cancellationToken);

        await context.RepoIndexes.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<RepoIndex>(Builders<RepoIndex>.IndexKeys.Ascending(i => i.UserId), new CreateIndexOptions { Name = "ix_user" }),
        ], cancellationToken);

        await context.SummaryJobs.Indexes.CreateManyAsync(
        [
            // At most one queued or processing job per repository: the database enforces idempotency.
            new CreateIndexModel<SummaryJob>(
                Builders<SummaryJob>.IndexKeys.Ascending(j => j.RepoId),
                new CreateIndexOptions<SummaryJob>
                {
                    Unique = true,
                    Name = "ux_active_job_per_repo",
                    PartialFilterExpression = Builders<SummaryJob>.Filter.Eq(j => j.IsActive, true),
                }),
            new CreateIndexModel<SummaryJob>(
                Builders<SummaryJob>.IndexKeys.Ascending(j => j.UserId).Ascending(j => j.RepoId).Descending(j => j.CreatedAt),
                new CreateIndexOptions { Name = "ix_user_repo_created" }),
            new CreateIndexModel<SummaryJob>(
                Builders<SummaryJob>.IndexKeys.Ascending(j => j.RepoId).Ascending(j => j.Status).Ascending(j => j.CommitSha).Descending(j => j.CompletedAt),
                new CreateIndexOptions { Name = "ix_repo_status_commit" }),
            new CreateIndexModel<SummaryJob>(
                Builders<SummaryJob>.IndexKeys.Ascending(j => j.Status).Ascending(j => j.PublishedAt).Ascending(j => j.CreatedAt),
                new CreateIndexOptions { Name = "ix_outbox" }),
            new CreateIndexModel<SummaryJob>(
                Builders<SummaryJob>.IndexKeys.Ascending(j => j.Status).Ascending(j => j.HeartbeatAt),
                new CreateIndexOptions { Name = "ix_heartbeat" }),
        ], cancellationToken);

        await context.CodeChunks.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<CodeChunk>(
                Builders<CodeChunk>.IndexKeys.Ascending(c => c.RepoId).Ascending(c => c.CommitSha),
                new CreateIndexOptions { Name = "ix_repo_commit" }),
        ], cancellationToken);

        await context.Conversations.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Conversation>(
                Builders<Conversation>.IndexKeys.Ascending(c => c.UserId).Ascending(c => c.RepoId).Descending(c => c.UpdatedAt),
                new CreateIndexOptions { Name = "ix_user_repo_updated" }),
            new CreateIndexModel<Conversation>(Builders<Conversation>.IndexKeys.Ascending(c => c.RepoId), new CreateIndexOptions { Name = "ix_repo" }),
        ], cancellationToken);

        await context.AuthCodes.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<AuthCode>(Builders<AuthCode>.IndexKeys.Ascending(c => c.ExpiresAt), new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "ttl_expires" }),
        ], cancellationToken);

        await context.RefreshTokens.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<RefreshToken>(Builders<RefreshToken>.IndexKeys.Ascending(t => t.ExpiresAt), new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "ttl_expires" }),
            new CreateIndexModel<RefreshToken>(Builders<RefreshToken>.IndexKeys.Ascending(t => t.FamilyId), new CreateIndexOptions { Name = "ix_family" }),
        ], cancellationToken);

        LogIndexesReady(context.Database.DatabaseNamespace.DatabaseName);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "MongoDB indexes are ready in database {Database}")]
    private partial void LogIndexesReady(string database);
}
