using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Errors;
using RepoLens.Domain.Users;

namespace RepoLens.Application.Users;

public sealed record UserDto(
    Guid Id,
    long GitHubId,
    string Login,
    string? Name,
    string? Email,
    string? AvatarUrl,
    DateTimeOffset? ReposSyncedAt,
    DateTimeOffset CreatedAt)
{
    public static UserDto From(User user) => new(
        user.Id, user.GitHubId, user.Login, user.Name, user.Email, user.AvatarUrl, user.ReposSyncedAt, user.CreatedAt);
}

public sealed class UserService(IUserRepository users)
{
    public async Task<UserDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken) ?? throw UserMissing();
        return UserDto.From(user);
    }

    internal static UnauthorizedException UserMissing() =>
        new("user_not_found", "This account no longer exists. Sign in with GitHub again.");
}
