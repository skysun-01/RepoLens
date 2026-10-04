namespace RepoLens.Domain.Conversations;

public sealed class ConversationMessage
{
    private ConversationMessage()
    {
    }

    private ConversationMessage(Guid id, MessageRole role, string content, IReadOnlyList<Citation> citations, DateTimeOffset createdAt)
    {
        Id = id;
        Role = role;
        Content = content;
        Citations = citations;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public MessageRole Role { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public IReadOnlyList<Citation> Citations { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    internal static ConversationMessage Create(MessageRole role, string content, IReadOnlyList<Citation> citations, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(createdAt), role, content, citations, createdAt);
}
