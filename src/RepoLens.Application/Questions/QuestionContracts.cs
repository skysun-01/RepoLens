using RepoLens.Domain.Conversations;

namespace RepoLens.Application.Questions;

/// <param name="Question">What the user typed.</param>
/// <param name="ConversationId">Set to ask a follow-up in an existing conversation.</param>
public sealed record AskQuestionRequest(string Question, Guid? ConversationId);

public sealed record CitationDto(string Path, int StartLine, int EndLine, string Url)
{
    public static CitationDto From(Citation citation) => new(citation.Path, citation.StartLine, citation.EndLine, citation.Url);
}

public sealed record AnswerDto(
    Guid ConversationId,
    Guid QuestionMessageId,
    Guid AnswerMessageId,
    string Answer,
    IReadOnlyList<CitationDto> Citations,
    string CommitSha,
    DateTimeOffset CreatedAt);

/// <summary>A prepared answer, ready to stream. Enumerate <see cref="ReadAllAsync"/> once.</summary>
public sealed class AnswerStream(Func<CancellationToken, IAsyncEnumerable<AnswerStreamEvent>> read)
{
    public IAsyncEnumerable<AnswerStreamEvent> ReadAllAsync(CancellationToken cancellationToken) => read(cancellationToken);
}

/// <summary>Events of a streamed answer, sent to the client as Server-Sent Events.</summary>
public abstract record AnswerStreamEvent(string EventType);

/// <summary>First event: which conversation the answer belongs to.</summary>
public sealed record AnswerStarted(Guid ConversationId, string CommitSha) : AnswerStreamEvent("start");

/// <summary>A piece of the answer text, in order.</summary>
public sealed record AnswerDelta(string Text) : AnswerStreamEvent("delta");

/// <summary>Last event: the saved answer with its citations.</summary>
public sealed record AnswerCompleted(AnswerDto Answer) : AnswerStreamEvent("done");

public sealed record ConversationSummaryDto(Guid Id, Guid RepoId, string Title, int MessageCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ConversationMessageDto(Guid Id, MessageRole Role, string Content, IReadOnlyList<CitationDto> Citations, DateTimeOffset CreatedAt);

public sealed record ConversationDto(
    Guid Id,
    Guid RepoId,
    string Title,
    IReadOnlyList<ConversationMessageDto> Messages,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ConversationDto From(Conversation conversation) => new(
        conversation.Id,
        conversation.RepoId,
        conversation.Title,
        conversation.Messages
            .Select(m => new ConversationMessageDto(m.Id, m.Role, m.Content, m.Citations.Select(CitationDto.From).ToList(), m.CreatedAt))
            .ToList(),
        conversation.CreatedAt,
        conversation.UpdatedAt);
}
