using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Abstractions.Search;
using RepoLens.Application.Common.Errors;
using RepoLens.Application.Indexing;
using RepoLens.Domain.Conversations;
using RepoLens.Domain.Repos;

namespace RepoLens.Application.Questions;

/// <summary>
/// Answers questions about a repository with retrieval-augmented generation: embed the question,
/// find the closest code chunks of the indexed commit, and ask the model to answer from them with citations.
/// </summary>
public sealed class QuestionAnsweringService(
    IRepoRepository repos,
    IRepoIndexRepository indexes,
    ISummaryJobRepository jobs,
    IConversationRepository conversations,
    ICodeSearch codeSearch,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IChatClient chatClient,
    IOptions<QuestionOptions> options,
    TimeProvider timeProvider)
{
    public async Task<AnswerDto> AskAsync(Guid userId, Guid repoId, AskQuestionRequest request, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(userId, repoId, request, cancellationToken);
        var response = await chatClient.GetResponseAsync(prepared.Messages, prepared.ChatOptions, cancellationToken);
        return await SaveAsync(prepared, response.Text, cancellationToken);
    }

    /// <summary>
    /// Validates the question and retrieves sources before anything is streamed, so errors such as an
    /// unknown repository still become a normal error response. Then returns the stream of answer events.
    /// </summary>
    public async Task<AnswerStream> StartStreamAsync(Guid userId, Guid repoId, AskQuestionRequest request, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(userId, repoId, request, cancellationToken);
        return new AnswerStream(ct => StreamAsync(prepared, ct));
    }

    private async IAsyncEnumerable<AnswerStreamEvent> StreamAsync(PreparedQuestion prepared, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new AnswerStarted(prepared.Conversation.Id, prepared.Index.CommitSha);

        var answer = new StringBuilder();
        await foreach (var update in chatClient.GetStreamingResponseAsync(prepared.Messages, prepared.ChatOptions, cancellationToken))
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
            {
                answer.Append(text);
                yield return new AnswerDelta(text);
            }
        }

        yield return new AnswerCompleted(await SaveAsync(prepared, answer.ToString(), cancellationToken));
    }

    private async Task<PreparedQuestion> PrepareAsync(Guid userId, Guid repoId, AskQuestionRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var question = Validate(request, settings);

        var repo = await repos.GetAsync(userId, repoId, cancellationToken) ?? throw new NotFoundException("Repository", repoId);
        var index = await indexes.GetAsync(repo.Id, cancellationToken)
            ?? throw new ConflictException(
                "repo_not_indexed",
                "This repository has not been summarized yet. Run Summarize first; questions use the code index it builds.");

        if (index.EmbeddingModel != EmbeddingModel.IdOf(embeddingGenerator))
        {
            throw new ConflictException(
                "repo_index_outdated",
                "This repository was indexed with a different embedding model. Run Summarize again with force=true to rebuild the index.");
        }

        var conversation = await LoadOrStartConversationAsync(userId, repo, request.ConversationId, question, cancellationToken);
        var history = conversation.RecentMessages(settings.HistoryMessages);

        var queryVector = await embeddingGenerator.GenerateVectorAsync(QuestionPrompts.RetrievalQuery(history, question), cancellationToken: cancellationToken);
        var sources = await codeSearch.SearchAsync(repo.Id, index.CommitSha, queryVector, settings.TopK, cancellationToken);
        var summary = (await jobs.GetLatestCompletedAsync(repo.Id, commitSha: null, cancellationToken))?.Summary;

        var messages = QuestionPrompts.Build(repo, summary, sources, history, question);
        var chatOptions = new ChatOptions { MaxOutputTokens = settings.MaxAnswerTokens };

        return new PreparedQuestion(repo, index, conversation, question, sources, messages, chatOptions, timeProvider.GetUtcNow());
    }

    private async Task<Conversation> LoadOrStartConversationAsync(Guid userId, Repo repo, Guid? conversationId, string question, CancellationToken cancellationToken)
    {
        if (conversationId is null)
        {
            return Conversation.Start(userId, repo.Id, question, timeProvider.GetUtcNow());
        }

        var conversation = await conversations.GetAsync(userId, conversationId.Value, cancellationToken)
            ?? throw new NotFoundException("Conversation", conversationId.Value);

        if (!conversation.BelongsTo(userId, repo.Id))
        {
            throw new ValidationFailedException(
                "This conversation is about a different repository.",
                new Dictionary<string, string[]> { ["conversationId"] = ["The conversation belongs to another repository."] });
        }

        return conversation;
    }

    private async Task<AnswerDto> SaveAsync(PreparedQuestion prepared, string answer, CancellationToken cancellationToken)
    {
        var citations = CitationMapper.FromAnswer(answer, prepared.Repo, prepared.Index.CommitSha, prepared.Sources);
        var conversation = prepared.Conversation;
        var isNew = conversation.Version == 0 && conversation.Messages.Count == 0;

        for (var attempt = 1; ; attempt++)
        {
            var (questionMessage, answerMessage) = conversation.AddExchange(
                prepared.Question, answer, citations, prepared.AskedAt, timeProvider.GetUtcNow());

            try
            {
                if (isNew)
                {
                    await conversations.AddAsync(conversation, cancellationToken);
                }
                else
                {
                    await conversations.UpdateAsync(conversation, cancellationToken);
                }

                return new AnswerDto(
                    conversation.Id,
                    questionMessage.Id,
                    answerMessage.Id,
                    answer,
                    citations.Select(CitationDto.From).ToList(),
                    prepared.Index.CommitSha,
                    answerMessage.CreatedAt);
            }
            catch (ConcurrencyException) when (!isNew && attempt < 3)
            {
                // Another question in the same conversation was saved first; append to the latest version.
                conversation = await conversations.GetAsync(conversation.UserId, conversation.Id, cancellationToken)
                    ?? throw new NotFoundException("Conversation", conversation.Id);
            }
        }
    }

    private static string Validate(AskQuestionRequest request, QuestionOptions settings)
    {
        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            throw new ValidationFailedException("Type a question first.", new Dictionary<string, string[]> { ["question"] = ["The question is required."] });
        }

        if (question.Length > settings.MaxQuestionLength)
        {
            throw new ValidationFailedException(
                "The question is too long.",
                new Dictionary<string, string[]> { ["question"] = [$"Questions can be at most {settings.MaxQuestionLength} characters."] });
        }

        return question;
    }

    private sealed record PreparedQuestion(
        Repo Repo,
        RepoIndex Index,
        Conversation Conversation,
        string Question,
        IReadOnlyList<ScoredChunk> Sources,
        IReadOnlyList<ChatMessage> Messages,
        ChatOptions ChatOptions,
        DateTimeOffset AskedAt);
}
