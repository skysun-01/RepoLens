using RepoLens.Domain.Common;

namespace RepoLens.Domain.Conversations;

/// <summary>A thread of questions and answers about one repository.</summary>
public sealed class Conversation : Entity
{
    /// <summary>Oldest messages are dropped beyond this, keeping documents well below MongoDB's 16 MB limit.</summary>
    public const int MaxMessages = 200;

    public const int MaxTitleLength = 80;

    // Not readonly: the MongoDB driver assigns this field when loading a document.
    private List<ConversationMessage> _messages = [];

    private Conversation()
    {
    }

    private Conversation(Guid id, Guid userId, Guid repoId, string title, DateTimeOffset now)
        : base(id, now)
    {
        UserId = userId;
        RepoId = repoId;
        Title = title;
    }

    public Guid UserId { get; private set; }

    public Guid RepoId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public IReadOnlyList<ConversationMessage> Messages => _messages;

    public static Conversation Start(Guid userId, Guid repoId, string firstQuestion, DateTimeOffset now)
    {
        if (userId == Guid.Empty || repoId == Guid.Empty)
        {
            throw new DomainException("A conversation needs a user and a repository.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(firstQuestion);
        return new Conversation(Guid.CreateVersion7(now), userId, repoId, CreateTitle(firstQuestion), now);
    }

    public bool BelongsTo(Guid userId, Guid repoId) => UserId == userId && RepoId == repoId;

    /// <summary>The most recent messages, oldest first, for use as model context.</summary>
    public IReadOnlyList<ConversationMessage> RecentMessages(int count) =>
        count <= 0 ? [] : _messages.TakeLast(count).ToList();

    /// <summary>Records one completed question-and-answer exchange.</summary>
    public (ConversationMessage Question, ConversationMessage Answer) AddExchange(
        string question,
        string answer,
        IReadOnlyList<Citation> citations,
        DateTimeOffset askedAt,
        DateTimeOffset answeredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(citations);

        var questionMessage = ConversationMessage.Create(MessageRole.User, question, [], askedAt);
        var answerMessage = ConversationMessage.Create(MessageRole.Assistant, answer, citations, answeredAt);

        _messages.Add(questionMessage);
        _messages.Add(answerMessage);

        var overflow = _messages.Count - MaxMessages;
        if (overflow > 0)
        {
            _messages.RemoveRange(0, overflow);
        }

        Touch(answeredAt);
        return (questionMessage, answerMessage);
    }

    private static string CreateTitle(string question)
    {
        var singleLine = string.Join(' ', question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= MaxTitleLength ? singleLine : string.Concat(singleLine.AsSpan(0, MaxTitleLength - 1), "…");
    }
}
