namespace RepoLens.Domain.Common;

/// <summary>
/// Base class for aggregate roots: identity, audit timestamps and an optimistic-concurrency version.
/// </summary>
public abstract class Entity
{
    protected Entity(Guid id, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Entity id must not be empty.");
        }

        Id = id;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>Used by the persistence layer when materializing documents.</summary>
    protected Entity()
    {
    }

    public Guid Id { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Incremented by the persistence layer on every successful save. A save that expects an older
    /// version fails, which stops two writers from silently overwriting each other.
    /// </summary>
    public int Version { get; private set; }

    internal void IncrementVersion() => Version++;

    internal void RestoreVersion(int version) => Version = version;

    protected void Touch(DateTimeOffset now) => UpdatedAt = now;
}
