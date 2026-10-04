using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RepoLens.Api.Auth;
using RepoLens.Api.Contracts;
using RepoLens.Api.RateLimiting;
using RepoLens.Api.Streaming;
using RepoLens.Application.Questions;

namespace RepoLens.Api.Controllers;

/// <summary>The question box: answers grounded in the repository's code, with citations.</summary>
[ApiController]
[Authorize]
[Route("api/v1/repos/{repoId:guid}/questions")]
[EnableRateLimiting(RateLimitingSetup.Questions)]
public sealed class QuestionsController(QuestionAnsweringService answering, ILogger<QuestionsController> logger) : ControllerBase
{
    /// <summary>Asks a question about the repository and returns the full answer.</summary>
    /// <remarks>
    /// The repository must have been summarized first (409 <c>repo_not_indexed</c> otherwise): the Summarize job
    /// builds the code index answers are retrieved from. Pass the returned <c>conversationId</c> to ask a follow-up.
    /// </remarks>
    [HttpPost]
    [Produces("application/json")]
    [ProducesResponseType<AnswerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AnswerDto> Ask(Guid repoId, AskQuestionBody body, CancellationToken cancellationToken) =>
        answering.AskAsync(User.GetUserId(), repoId, body.ToRequest(), cancellationToken);

    /// <summary>Asks a question and streams the answer as Server-Sent Events.</summary>
    /// <remarks>
    /// Events: <c>start</c> { conversationId, commitSha }, then <c>delta</c> { text } for each piece of the answer,
    /// then <c>done</c> with the saved answer and its citations. A failure mid-way sends <c>error</c> { code, message }.
    /// Errors before streaming starts (unknown repository, not indexed) are normal Problem Details responses.
    /// </remarks>
    [HttpPost("stream")]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IResult> Stream(Guid repoId, AskQuestionBody body, CancellationToken cancellationToken)
    {
        var stream = await answering.StartStreamAsync(User.GetUserId(), repoId, body.ToRequest(), cancellationToken);
        return TypedResults.ServerSentEvents(AnswerServerSentEvents.From(stream.ReadAllAsync(cancellationToken), logger, cancellationToken));
    }
}
