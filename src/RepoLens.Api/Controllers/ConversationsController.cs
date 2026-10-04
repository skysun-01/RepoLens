using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RepoLens.Api.Auth;
using RepoLens.Application.Questions;

namespace RepoLens.Api.Controllers;

/// <summary>Question-and-answer history.</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
[Produces("application/json")]
public sealed class ConversationsController(ConversationService conversations) : ControllerBase
{
    /// <summary>Conversations about a repository, most recent first.</summary>
    [HttpGet("repos/{repoId:guid}/conversations")]
    [ProducesResponseType<IReadOnlyList<ConversationSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<ConversationSummaryDto>> List(Guid repoId, CancellationToken cancellationToken) =>
        conversations.ListAsync(User.GetUserId(), repoId, cancellationToken);

    /// <summary>One conversation with all its messages and citations.</summary>
    [HttpGet("conversations/{conversationId:guid}")]
    [ProducesResponseType<ConversationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ConversationDto> Get(Guid conversationId, CancellationToken cancellationToken) =>
        conversations.GetAsync(User.GetUserId(), conversationId, cancellationToken);

    /// <summary>Deletes a conversation.</summary>
    [HttpDelete("conversations/{conversationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid conversationId, CancellationToken cancellationToken)
    {
        await conversations.DeleteAsync(User.GetUserId(), conversationId, cancellationToken);
        return NoContent();
    }
}
