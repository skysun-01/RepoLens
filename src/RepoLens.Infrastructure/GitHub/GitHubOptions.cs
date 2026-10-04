using System.ComponentModel.DataAnnotations;

namespace RepoLens.Infrastructure.GitHub;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    /// <summary>OAuth App client id (GitHub → Settings → Developer settings → OAuth Apps).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth App client secret. Keep it in user secrets or Key Vault.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Scopes requested at sign-in. <c>repo</c> is needed to read private repositories.</summary>
    public string[] Scopes { get; set; } = ["read:user", "user:email", "repo"];

    [Required]
    [Url]
    public string ApiBaseUrl { get; set; } = "https://api.github.com/";

    [Required]
    public string UserAgent { get; set; } = "RepoLens";

    [Range(1, 10_000)]
    public int MaxRepositories { get; set; } = 2_000;

    /// <summary>Largest repository archive the worker downloads.</summary>
    [Range(1_048_576, 4_294_967_296)]
    public long MaxArchiveBytes { get; set; } = 300L * 1024 * 1024;
}
