using RepoLens.Domain.Conversations;

namespace RepoLens.Application.Common.Abstractions.Persistence;

public interface IConversationRepository
{
    /// <summary>Returns the conversation only if it belongs to <paramref name="userId"/>.</summary>
    Task<Conversation?> GetAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);

    /// <summary>Conversation headers (no messages), newest first.</summary>
    Task<IReadOnlyList<ConversationHeader>> ListAsync(Guid userId, Guid repoId, int limit, CancellationToken cancellationToken);

    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);

    /// <summary>Saves changes; throws <see cref="Errors.ConcurrencyException"/> if someone else saved first.</summary>
    Task UpdateAsync(Conversation conversation, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);

    Task DeleteForReposAsync(IReadOnlyCollection<Guid> repoIds, CancellationToken cancellationToken);
}

public sealed record ConversationHeader(Guid Id, Guid RepoId, string Title, int MessageCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
