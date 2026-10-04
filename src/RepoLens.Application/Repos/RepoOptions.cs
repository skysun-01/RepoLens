using System.ComponentModel.DataAnnotations;

namespace RepoLens.Application.Repos;

public sealed class RepoOptions
{
    public const string SectionName = "Repos";

    /// <summary>The repo list is synced from GitHub automatically when the last sync is older than this.</summary>
    [Range(typeof(TimeSpan), "00:00:00", "7.00:00:00")]
    public TimeSpan AutoSyncAfter { get; set; } = TimeSpan.FromHours(6);

    [Range(1, 100)]
    public int DefaultPageSize { get; set; } = 25;

    [Range(1, 100)]
    public int MaxPageSize { get; set; } = 100;
}
