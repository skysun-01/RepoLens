using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Errors;

namespace RepoLens.Application.Questions;

public sealed class ConversationService(IRepoRepository repos, IConversationRepository conversations)
{
    private const int ListLimit = 100;

    public async Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(Guid userId, Guid repoId, CancellationToken cancellationToken)
    {
        _ = await repos.GetAsync(userId, repoId, cancellationToken) ?? throw new NotFoundException("Repository", repoId);
        var headers = await conversations.ListAsync(userId, repoId, ListLimit, cancellationToken);
        return headers.Select(h => new ConversationSummaryDto(h.Id, h.RepoId, h.Title, h.MessageCount, h.CreatedAt, h.UpdatedAt)).ToList();
    }

    public async Task<ConversationDto> GetAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(userId, conversationId, cancellationToken)
            ?? throw new NotFoundException("Conversation", conversationId);
        return ConversationDto.From(conversation);
    }

    public async Task DeleteAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        if (!await conversations.DeleteAsync(userId, conversationId, cancellationToken))
        {
            throw new NotFoundException("Conversation", conversationId);
        }
    }
}
