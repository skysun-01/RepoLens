using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RepoLens.Api.Auth;
using RepoLens.Application.Users;

namespace RepoLens.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/me")]
[Produces("application/json")]
public sealed class MeController(UserService users) : ControllerBase
{
    /// <summary>The signed-in user's profile.</summary>
    [HttpGet]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public Task<UserDto> Get(CancellationToken cancellationToken) =>
        users.GetProfileAsync(User.GetUserId(), cancellationToken);
}
