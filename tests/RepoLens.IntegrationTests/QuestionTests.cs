using System.Net;
using System.Net.Http.Json;
using RepoLens.Application.Questions;
using RepoLens.IntegrationTests.Infrastructure;

namespace RepoLens.IntegrationTests;

/// <summary>The question box: retrieval over the index the Summarize job built, with citations.</summary>
public sealed class QuestionTests(RepoLensApiFactory factory) : IClassFixture<RepoLensApiFactory>
{
    [Fact]
    public async Task Questions_need_the_repository_to_be_summarized_first()
    {
        var client = await factory.CreateSignedInClientAsync();
        var shop = await client.GetRepoByNameAsync("shop");

        var response = await client.PostAsJsonAsync($"/api/v1/repos/{shop.Id}/questions", new { question = "Where are orders created?" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("repo_not_indexed", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Answers_cite_source_lines_and_follow_ups_continue_the_conversation()
    {
        var client = await factory.CreateSignedInClientAsync();
        var shop = await client.GetRepoByNameAsync("shop");
        var job = await client.SummarizeAsync(shop.Id);

        var first = await (await client.PostAsJsonAsync($"/api/v1/repos/{shop.Id}/questions", new { question = "Where are orders created?" }))
            .ReadAsync<AnswerDto>();

        Assert.Equal(FakeChatClient.Answer, first.Answer);
        Assert.Equal(job.CommitSha, first.CommitSha);
        Assert.Equal(2, first.Citations.Count);
        Assert.All(first.Citations, c => Assert.StartsWith($"https://github.com/octo/shop/blob/{job.CommitSha}/", c.Url, StringComparison.Ordinal));
        Assert.All(first.Citations, c => Assert.Contains($"#L{c.StartLine}-L{c.EndLine}", c.Url, StringComparison.Ordinal));
        Assert.DoesNotContain(first.Citations, c => c.Path.StartsWith("node_modules/", StringComparison.Ordinal));

        var followUp = await (await client.PostAsJsonAsync($"/api/v1/repos/{shop.Id}/questions", new { question = "And how is it tested?", conversationId = first.ConversationId }))
            .ReadAsync<AnswerDto>();
        Assert.Equal(first.ConversationId, followUp.ConversationId);

        var conversation = await client.GetFromJsonAsync<ConversationDto>($"/api/v1/conversations/{first.ConversationId}", RepoLensApiFactory.Json);
        Assert.Equal(4, conversation!.Messages.Count);
        Assert.Equal("Where are orders created?", conversation.Title);

        var list = await client.GetFromJsonAsync<List<ConversationSummaryDto>>($"/api/v1/repos/{shop.Id}/conversations", RepoLensApiFactory.Json);
        Assert.Equal(4, Assert.Single(list!).MessageCount);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/conversations/{first.ConversationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/conversations/{first.ConversationId}")).StatusCode);
    }

    [Fact]
    public async Task Streaming_answer_sends_start_delta_and_done_events()
    {
        var client = await factory.CreateSignedInClientAsync();
        var shop = await client.GetRepoByNameAsync("shop");
        await client.SummarizeAsync(shop.Id);

        var response = await client.PostAsJsonAsync($"/api/v1/repos/{shop.Id}/questions/stream", new { question = "Where are orders created?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("event: start", body, StringComparison.Ordinal);
        Assert.Contains("event: delta", body, StringComparison.Ordinal);
        Assert.Contains("event: done", body, StringComparison.Ordinal);
        Assert.Contains("\"citations\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Streaming_errors_before_the_stream_starts_are_problem_details()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/repos/{Guid.NewGuid()}/questions/stream", new { question = "Anything?" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_questions_are_rejected(string question)
    {
        var client = await factory.CreateSignedInClientAsync();
        var shop = await client.GetRepoByNameAsync("shop");

        var response = await client.PostAsJsonAsync($"/api/v1/repos/{shop.Id}/questions", new { question });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
