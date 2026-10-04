using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RepoLens.Api.Auth;
using RepoLens.Api.RateLimiting;
using RepoLens.Application.Common.Models;
using RepoLens.Application.Repos;

namespace RepoLens.Api.Controllers;

/// <summary>The repository table.</summary>
[ApiController]
[Authorize]
[Route("api/v1/repos")]
[Produces("application/json")]
public sealed class ReposController(RepoQueryService queries, RepoSyncService sync) : ControllerBase
{
    /// <summary>Lists every repository you can access on GitHub, with its summary status and latest PDF link.</summary>
    /// <remarks>The list is synced from GitHub automatically on first use and when it is older than Repos:AutoSyncAfter.</remarks>
    /// <param name="page">1-based page number. Default 1.</param>
    /// <param name="pageSize">Rows per page, 1–100. Default 25.</param>
    /// <param name="search">Matches repository name, description or language.</param>
    /// <param name="sort"><c>pushed</c> (default, most recent first), <c>name</c> or <c>stars</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<RepoDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<RepoDto>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] string? sort,
        CancellationToken cancellationToken) =>
        queries.ListAsync(User.GetUserId(), new RepoListQuery(page, pageSize, search, sort), cancellationToken);

    /// <summary>Refreshes the repository list from GitHub now.</summary>
    [HttpPost("sync")]
    [EnableRateLimiting(RateLimitingSetup.Sync)]
    [ProducesResponseType<RepoSyncResultDto>(StatusCodes.Status200OK)]
    public Task<RepoSyncResultDto> Sync(CancellationToken cancellationToken) =>
        sync.SyncAsync(User.GetUserId(), cancellationToken);

    /// <summary>One repository.</summary>
    [HttpGet("{repoId:guid}")]
    [ProducesResponseType<RepoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RepoDto> Get(Guid repoId, CancellationToken cancellationToken) =>
        queries.GetAsync(User.GetUserId(), repoId, cancellationToken);
}
