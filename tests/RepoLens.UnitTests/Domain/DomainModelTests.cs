using RepoLens.Domain.Auth;
using RepoLens.Domain.Conversations;
using RepoLens.Domain.Indexing;
using RepoLens.Domain.Repos;

namespace RepoLens.UnitTests.Domain;

public sealed class DomainModelTests
{
    [Fact]
    public void Repo_UpdateFrom_reports_no_change_for_identical_details()
    {
        var repo = TestData.Repo();

        Assert.False(repo.UpdateFrom(TestData.Details(), TestData.Now.AddHours(1)));
        Assert.True(repo.UpdateFrom(TestData.Details() with { Stars = 99 }, TestData.Now.AddHours(1)));
        Assert.Equal(99, repo.Stars);
    }

    [Fact]
    public void Repo_UpdateFrom_rejects_details_of_another_repository()
    {
        var repo = TestData.Repo();

        Assert.Throws<RepoLens.Domain.Common.DomainException>(() => repo.UpdateFrom(TestData.Details(id: 7), TestData.Now));
    }

    [Fact]
    public void Conversation_title_is_the_question_on_one_line_and_truncated()
    {
        var question = "How does\n   checkout   work? " + new string('x', 200);

        var conversation = Conversation.Start(Guid.NewGuid(), Guid.NewGuid(), question, TestData.Now);

        Assert.StartsWith("How does checkout work?", conversation.Title, StringComparison.Ordinal);
        Assert.Equal(Conversation.MaxTitleLength, conversation.Title.Length);
    }

    [Fact]
    public void Conversation_keeps_only_the_most_recent_messages()
    {
        var conversation = Conversation.Start(Guid.NewGuid(), Guid.NewGuid(), "q", TestData.Now);

        for (var i = 0; i < Conversation.MaxMessages; i++)
        {
            conversation.AddExchange($"q{i}", $"a{i}", [], TestData.Now, TestData.Now);
        }

        Assert.Equal(Conversation.MaxMessages, conversation.Messages.Count);
        Assert.Equal($"a{Conversation.MaxMessages - 1}", conversation.Messages[^1].Content);
        Assert.Equal(2, conversation.RecentMessages(2).Count);
    }

    [Fact]
    public void Refresh_token_is_inactive_once_revoked_or_expired()
    {
        var token = new RefreshToken("hash", Guid.NewGuid(), Guid.NewGuid(), TestData.Now, TestData.Now.AddDays(1));

        Assert.True(token.IsActive(TestData.Now));
        Assert.False(token.IsActive(TestData.Now.AddDays(2)));

        token.Revoke(TestData.Now, "next");
        Assert.False(token.IsActive(TestData.Now));
        Assert.Equal("next", token.ReplacedByHash);
    }

    [Fact]
    public void Code_chunk_ids_are_deterministic()
    {
        var repoId = Guid.NewGuid();
        var a = new CodeChunk(repoId, TestData.Sha, "src/a.cs", "csharp", 1, 10, "x");
        var b = new CodeChunk(repoId, TestData.Sha, "src/a.cs", "csharp", 1, 10, "different content");

        Assert.Equal(a.Id, b.Id);
        Assert.NotEqual(a.Id, new CodeChunk(repoId, TestData.Sha, "src/a.cs", "csharp", 11, 20, "x").Id);
    }
}
