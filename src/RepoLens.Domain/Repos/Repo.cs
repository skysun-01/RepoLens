using RepoLens.Domain.Common;

namespace RepoLens.Domain.Repos;

/// <summary>
/// A GitHub repository the user can access, mirrored from GitHub. Only repository sync writes it;
/// summary status and search-index state live in their own collections so writers never collide.
/// </summary>
public sealed class Repo : Entity
{
    private Repo()
    {
    }

    private Repo(Guid id, Guid userId, long gitHubRepoId, DateTimeOffset now)
        : base(id, now)
    {
        UserId = userId;
        GitHubRepoId = gitHubRepoId;
    }

    public Guid UserId { get; private set; }

    public long GitHubRepoId { get; private set; }

    public string Owner { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string FullName { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? Language { get; private set; }

    public bool IsPrivate { get; private set; }

    public bool IsFork { get; private set; }

    public bool IsArchived { get; private set; }

    public string DefaultBranch { get; private set; } = "main";

    public string HtmlUrl { get; private set; } = string.Empty;

    public int Stars { get; private set; }

    public long SizeKb { get; private set; }

    public DateTimeOffset? PushedAt { get; private set; }

    public static Repo Create(Guid userId, RepoDetails details, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("A repository must belong to a user.");
        }

        var repo = new Repo(Guid.CreateVersion7(now), userId, details.GitHubRepoId, now);
        repo.Apply(details);
        return repo;
    }

    /// <summary>Applies the latest metadata from GitHub. Returns false when nothing changed.</summary>
    public bool UpdateFrom(RepoDetails details, DateTimeOffset now)
    {
        if (details.GitHubRepoId != GitHubRepoId)
        {
            throw new DomainException("Cannot apply details of a different GitHub repository.");
        }

        if (details == ToDetails())
        {
            return false;
        }

        Apply(details);
        Touch(now);
        return true;
    }

    public RepoDetails ToDetails() => new(
        GitHubRepoId, Owner, Name, FullName, Description, Language, IsPrivate, IsFork, IsArchived,
        DefaultBranch, HtmlUrl, Stars, SizeKb, PushedAt);

    private void Apply(RepoDetails details)
    {
        if (string.IsNullOrWhiteSpace(details.Owner) || string.IsNullOrWhiteSpace(details.Name))
        {
            throw new DomainException("Repository owner and name are required.");
        }

        Owner = details.Owner;
        Name = details.Name;
        FullName = details.FullName;
        Description = details.Description;
        Language = details.Language;
        IsPrivate = details.IsPrivate;
        IsFork = details.IsFork;
        IsArchived = details.IsArchived;
        DefaultBranch = string.IsNullOrWhiteSpace(details.DefaultBranch) ? "main" : details.DefaultBranch;
        HtmlUrl = details.HtmlUrl;
        Stars = details.Stars;
        SizeKb = details.SizeKb;
        PushedAt = details.PushedAt;
    }
}
