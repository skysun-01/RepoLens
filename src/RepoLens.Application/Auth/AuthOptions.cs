using System.ComponentModel.DataAnnotations;

namespace RepoLens.Application.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>How long the one-time code handed to the frontend after GitHub sign-in stays valid.</summary>
    [Range(typeof(TimeSpan), "00:00:10", "00:10:00")]
    public TimeSpan AuthCodeLifetime { get; set; } = TimeSpan.FromSeconds(60);

    [Range(typeof(TimeSpan), "01:00:00", "90.00:00:00")]
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);
}
