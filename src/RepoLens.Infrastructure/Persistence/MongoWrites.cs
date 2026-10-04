using MongoDB.Driver;
using RepoLens.Application.Common.Errors;
using RepoLens.Domain.Common;

namespace RepoLens.Infrastructure.Persistence;

/// <summary>Insert and replace with optimistic concurrency on <see cref="Entity.Version"/>.</summary>
internal static class MongoWrites
{
    /// <summary>Inserts a new entity. A duplicate key (for example a unique index) surfaces as <see cref="ConcurrencyException"/>.</summary>
    public static async Task InsertAsync<T>(IMongoCollection<T> collection, T entity, CancellationToken cancellationToken)
        where T : Entity
    {
        var expected = entity.Version;
        entity.IncrementVersion();
        try
        {
            await collection.InsertOneAsync(entity, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (IsDuplicateKey(ex))
        {
            entity.RestoreVersion(expected);
            throw new ConcurrencyException(typeof(T).Name, entity.Id);
        }
        catch
        {
            entity.RestoreVersion(expected);
            throw;
        }
    }

    /// <summary>Replaces the stored document only if nobody saved a newer version in between.</summary>
    public static async Task ReplaceAsync<T>(IMongoCollection<T> collection, T entity, CancellationToken cancellationToken)
        where T : Entity
    {
        var expected = entity.Version;
        var filter = Builders<T>.Filter.Eq(e => e.Id, entity.Id) & Builders<T>.Filter.Eq(e => e.Version, expected);
        entity.IncrementVersion();

        ReplaceOneResult result;
        try
        {
            result = await collection.ReplaceOneAsync(filter, entity, cancellationToken: cancellationToken);
        }
        catch
        {
            entity.RestoreVersion(expected);
            throw;
        }

        if (result.MatchedCount == 0)
        {
            entity.RestoreVersion(expected);
            throw new ConcurrencyException(typeof(T).Name, entity.Id);
        }
    }

    public static bool IsDuplicateKey(MongoWriteException exception) =>
        exception.WriteError?.Category == ServerErrorCategory.DuplicateKey;

    public static bool OnlyDuplicateKeys(MongoBulkWriteException exception) =>
        exception.WriteErrors.Count > 0
        && exception.WriteErrors.All(e => e.Category == ServerErrorCategory.DuplicateKey)
        && exception.WriteConcernError is null;
}
