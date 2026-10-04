using MongoDB.Bson;
using MongoDB.Driver;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Domain.Conversations;

namespace RepoLens.Infrastructure.Persistence.Repositories;

internal sealed class MongoConversationRepository(MongoContext context) : IConversationRepository
{
    private IMongoCollection<Conversation> Conversations => context.Conversations;

    public async Task<Conversation?> GetAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        await Conversations.Find(c => c.Id == conversationId && c.UserId == userId).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ConversationHeader>> ListAsync(Guid userId, Guid repoId, int limit, CancellationToken cancellationToken)
    {
        // Project the message count instead of loading every message.
        var projection = new BsonDocument
        {
            { "repoId", 1 },
            { "title", 1 },
            { "createdAt", 1 },
            { "updatedAt", 1 },
            { "messageCount", new BsonDocument("$size", new BsonDocument("$ifNull", new BsonArray { "$messages", new BsonArray() })) },
        };

        var documents = await Conversations.Aggregate()
            .Match(c => c.UserId == userId && c.RepoId == repoId)
            .SortByDescending(c => c.UpdatedAt)
            .Limit(limit)
            .Project(projection)
            .ToListAsync(cancellationToken);

        return documents.Select(d => new ConversationHeader(
                d["_id"].AsGuid,
                d["repoId"].AsGuid,
                d["title"].AsString,
                d["messageCount"].ToInt32(),
                new DateTimeOffset(d["createdAt"].ToUniversalTime()),
                new DateTimeOffset(d["updatedAt"].ToUniversalTime())))
            .ToList();
    }

    public Task AddAsync(Conversation conversation, CancellationToken cancellationToken) =>
        MongoWrites.InsertAsync(Conversations, conversation, cancellationToken);

    public Task UpdateAsync(Conversation conversation, CancellationToken cancellationToken) =>
        MongoWrites.ReplaceAsync(Conversations, conversation, cancellationToken);

    public async Task<bool> DeleteAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await Conversations.DeleteOneAsync(c => c.Id == conversationId && c.UserId == userId, cancellationToken);
        return result.DeletedCount > 0;
    }

    public Task DeleteForReposAsync(IReadOnlyCollection<Guid> repoIds, CancellationToken cancellationToken) =>
        repoIds.Count == 0
            ? Task.CompletedTask
            : Conversations.DeleteManyAsync(Builders<Conversation>.Filter.In(c => c.RepoId, repoIds), cancellationToken);
}
