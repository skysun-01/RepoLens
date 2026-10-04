using System.ComponentModel.DataAnnotations;
using RepoLens.Application.Questions;
using RepoLens.Application.Summaries;

namespace RepoLens.Api.Contracts;

// MVC validates records through their constructor parameters, so attributes go there (not on "property:").
public sealed record ExchangeCodeRequest([Required] string Code);

public sealed record RefreshTokenRequest([Required] string RefreshToken);

/// <param name="Question">The question about the repository.</param>
/// <param name="ConversationId">Continue an existing conversation (follow-up question). Omit to start a new one.</param>
public sealed record AskQuestionBody(
    [Required, MaxLength(4_000)] string Question,
    Guid? ConversationId)
{
    public AskQuestionRequest ToRequest() => new(Question, ConversationId);
}

/// <param name="Job">The summary job (new, in progress, or the finished one being reused).</param>
/// <param name="Outcome">Created, AlreadyInProgress or ReusedCompleted.</param>
public sealed record SummaryRequestResponse(SummaryJobDto Job, SummaryRequestOutcome Outcome);
