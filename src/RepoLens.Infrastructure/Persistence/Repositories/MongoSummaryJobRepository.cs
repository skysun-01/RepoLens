using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Domain.Summaries;

namespace RepoLens.Infrastructure.Persistence.Repositories;

internal sealed class MongoSummaryJobRepository(MongoContext context) : ISummaryJobRepository
{
    private static FilterDefinitionBuilder<SummaryJob> Filter => Builders<SummaryJob>.Filter;

    private static UpdateDefinitionBuilder<SummaryJob> Update => Builders<SummaryJob>.Update;

    private IMongoCollection<SummaryJob> Jobs => context.SummaryJobs;

    public async Task<SummaryJob?> GetAsync(Guid jobId, CancellationToken cancellationToken) =>
        await Jobs.Find(j => j.Id == jobId).FirstOrDefaultAsync(cancellationToken);

    public async Task<SummaryJob?> GetForUserAsync(Guid userId, Guid jobId, CancellationToken cancellationToken) =>
        await Jobs.Find(j => j.Id == jobId && j.UserId == userId).FirstOrDefaultAsync(cancellationToken);

    public async Task<SummaryJob?> GetActiveForRepoAsync(Guid repoId, CancellationToken cancellationToken) =>
        await Jobs.Find(j => j.RepoId == repoId && j.IsActive).FirstOrDefaultAsync(cancellationToken);

    public async Task<SummaryJob?> GetLatestCompletedAsync(Guid repoId, string? commitSha, CancellationToken cancellationToken)
    {
        var filter = Filter.Eq(j => j.RepoId, repoId) & Filter.Eq(j => j.Status, SummaryStatus.Completed);
        if (commitSha is not null)
        {
            filter &= Filter.Eq(j => j.CommitSha, commitSha);
        }

        return await Jobs.Find(filter).SortByDescending(j => j.CompletedAt).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SummaryJob>> ListForRepoAsync(Guid userId, Guid repoId, int limit, CancellationToken cancellationToken) =>
        await Jobs.Find(j => j.UserId == userId && j.RepoId == repoId)
            .SortByDescending(j => j.CreatedAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, SummaryJob>> GetLatestPerRepoAsync(
        IReadOnlyCollection<Guid> repoIds,
        bool completedOnly,
        CancellationToken cancellationToken)
    {
        if (repoIds.Count == 0)
        {
            return new Dictionary<Guid, SummaryJob>();
        }

        var filter = Filter.In(j => j.RepoId, repoIds);
        if (completedOnly)
        {
            filter &= Filter.Eq(j => j.Status, SummaryStatus.Completed);
        }

        var latest = await Jobs.Aggregate()
            .Match(filter)
            .Sort(completedOnly ? Builders<SummaryJob>.Sort.Descending(j => j.CompletedAt) : Builders<SummaryJob>.Sort.Descending(j => j.CreatedAt))
            .Group(new BsonDocument
            {
                { "_id", "$repoId" },
                { "job", new BsonDocument("$first", "$$ROOT") },
            })
            .ToListAsync(cancellationToken);

        return latest
            .Select(doc => BsonSerializer.Deserialize<SummaryJob>(doc["job"].AsBsonDocument))
            .ToDictionary(j => j.RepoId);
    }

    public async Task<bool> TryAddAsync(SummaryJob job, CancellationToken cancellationToken)
    {
        try
        {
            await MongoWrites.InsertAsync(Jobs, job, cancellationToken);
            return true;
        }
        catch (Application.Common.Errors.ConcurrencyException)
        {
            // The unique partial index rejected a second active job for this repository.
            return false;
        }
    }

    public Task UpdateAsync(SummaryJob job, CancellationToken cancellationToken) =>
        MongoWrites.ReplaceAsync(Jobs, job, cancellationToken);

    public Task MarkPublishedAsync(Guid jobId, DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
        Jobs.UpdateOneAsync(
            Filter.Eq(j => j.Id, jobId) & Filter.Eq(j => j.PublishedAt, null),
            Update.Set(j => j.PublishedAt, publishedAt),
            cancellationToken: cancellationToken);

    public Task SaveProgressAsync(SummaryJob job, CancellationToken cancellationToken) =>
        Jobs.UpdateOneAsync(
            Filter.Eq(j => j.Id, job.Id) & Filter.Eq(j => j.Status, SummaryStatus.Processing),
            Update
                .Set(j => j.Stage, job.Stage)
                .Set(j => j.Progress, job.Progress)
                .Set(j => j.HeartbeatAt, job.HeartbeatAt)
                .Set(j => j.UpdatedAt, job.UpdatedAt),
            cancellationToken: cancellationToken);

    public Task TouchHeartbeatAsync(Guid jobId, DateTimeOffset heartbeatAt, CancellationToken cancellationToken) =>
        Jobs.UpdateOneAsync(
            Filter.Eq(j => j.Id, jobId) & Filter.Eq(j => j.Status, SummaryStatus.Processing),
            Update.Set(j => j.HeartbeatAt, heartbeatAt),
            cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<SummaryJob>> GetUnpublishedAsync(DateTimeOffset createdBefore, int limit, CancellationToken cancellationToken) =>
        await Jobs.Find(
                Filter.Eq(j => j.Status, SummaryStatus.Queued)
                & Filter.Eq(j => j.PublishedAt, null)
                & Filter.Lt(j => j.CreatedAt, createdBefore))
            .SortBy(j => j.CreatedAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SummaryJob>> GetStaleAsync(DateTimeOffset heartbeatBefore, int limit, CancellationToken cancellationToken) =>
        await Jobs.Find(Filter.Eq(j => j.Status, SummaryStatus.Processing) & Filter.Lt(j => j.HeartbeatAt, heartbeatBefore))
            .SortBy(j => j.HeartbeatAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);
}
