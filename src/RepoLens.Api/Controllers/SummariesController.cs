using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using RepoLens.Api.Auth;
using RepoLens.Api.Contracts;
using RepoLens.Api.RateLimiting;
using RepoLens.Application.Summaries;

namespace RepoLens.Api.Controllers;

/// <summary>The Summarize button: event-driven PDF generation.</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
[Produces("application/json")]
public sealed class SummariesController(SummaryRequestService requests, SummaryQueryService queries) : ControllerBase
{
    /// <summary>Summarizes a repository into an onboarding PDF.</summary>
    /// <remarks>
    /// Returns immediately; the PDF is generated in the background.
    /// <list type="bullet">
    /// <item><b>202 Accepted</b>: a job was queued (or one is already running). Poll the <c>Location</c> URL until <c>status</c> is <c>Completed</c>.</item>
    /// <item><b>200 OK</b>: this exact commit was already summarized; the existing PDF is returned.</item>
    /// </list>
    /// Pass <c>force=true</c> to regenerate anyway.
    /// </remarks>
    /// <param name="repoId">Repository id from <c>GET /api/v1/repos</c>.</param>
    /// <param name="force">Regenerate even if this commit already has a summary.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpPost("repos/{repoId:guid}/summaries")]
    [EnableRateLimiting(RateLimitingSetup.Summaries)]
    [ProducesResponseType<SummaryRequestResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<SummaryRequestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(Guid repoId, [FromQuery] bool force, CancellationToken cancellationToken)
    {
        var result = await requests.RequestAsync(User.GetUserId(), repoId, force, cancellationToken);
        var body = new SummaryRequestResponse(result.Job, result.Outcome);

        return result.Outcome == SummaryRequestOutcome.ReusedCompleted
            ? Ok(body)
            : AcceptedAtAction(nameof(Get), new { jobId = result.Job.Id }, body);
    }

    /// <summary>Summary history of a repository, newest first.</summary>
    [HttpGet("repos/{repoId:guid}/summaries")]
    [ProducesResponseType<IReadOnlyList<SummaryJobDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<SummaryJobDto>> List(Guid repoId, CancellationToken cancellationToken) =>
        queries.ListForRepoAsync(User.GetUserId(), repoId, cancellationToken);

    /// <summary>Status of a summary job: status, stage, progress (0–100) and, when finished, the PDF link.</summary>
    [HttpGet("summaries/{jobId:guid}")]
    [ProducesResponseType<SummaryJobDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SummaryJobDto> Get(Guid jobId, CancellationToken cancellationToken) =>
        queries.GetAsync(User.GetUserId(), jobId, cancellationToken);

    /// <summary>The summary PDF.</summary>
    /// <remarks>Shown inline (for a PDF viewer) by default; <c>download=true</c> asks the browser to save it.</remarks>
    /// <param name="jobId">Summary job id.</param>
    /// <param name="download">Send as an attachment instead of inline.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet("summaries/{jobId:guid}/pdf")]
    [Produces(MediaTypeNames.Application.Pdf, MediaTypeNames.Application.Json)]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK, MediaTypeNames.Application.Pdf)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pdf(Guid jobId, [FromQuery] bool download, CancellationToken cancellationToken)
    {
        var pdf = await queries.OpenPdfAsync(User.GetUserId(), jobId, cancellationToken);

        if (!download)
        {
            var disposition = new ContentDispositionHeaderValue("inline");
            disposition.SetHttpFileName(pdf.FileName);
            Response.Headers.ContentDisposition = disposition.ToString();
        }

        return new FileStreamResult(pdf.Content, pdf.ContentType)
        {
            FileDownloadName = download ? pdf.FileName : null,
            EntityTag = new EntityTagHeaderValue(pdf.ETag),
            EnableRangeProcessing = true,
        };
    }
}
