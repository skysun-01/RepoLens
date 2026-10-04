using System.IO.Compression;
using System.Text;
using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Domain.Repos;

namespace RepoLens.IntegrationTests.Infrastructure;

/// <summary>Stands in for GitHub: two repositories and a small, realistic source archive.</summary>
public sealed class FakeGitHubClient : IGitHubClient
{
    public const string RevokedToken = "gho_revoked";

    private int _archiveDownloads;

    public string HeadSha { get; set; } = "1111111111111111111111111111111111111111";

    /// <summary>Return an exception to make the archive download for a repository fail.</summary>
    public Func<string, int, Exception?> ArchiveFailure { get; set; } = (_, _) => null;

    public int ArchiveDownloads => Volatile.Read(ref _archiveDownloads);

    public static IReadOnlyList<RepoDetails> Repositories { get; } =
    [
        new(101, "octo", "shop", "octo/shop", "A sample e-commerce API", "C#", false, false, false, "main", "https://github.com/octo/shop", 12, 250, DateTimeOffset.Parse("2026-09-20T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture)),
        new(102, "octo", "blog", "octo/blog", "Personal blog", "TypeScript", true, false, false, "main", "https://github.com/octo/blog", 3, 80, DateTimeOffset.Parse("2026-09-10T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture)),
    ];

    public Task<IReadOnlyList<RepoDetails>> GetRepositoriesAsync(string accessToken, CancellationToken cancellationToken) =>
        accessToken == RevokedToken
            ? throw new GitHubAuthorizationException()
            : Task.FromResult(Repositories);

    public Task<string> GetBranchHeadShaAsync(string accessToken, string owner, string repo, string branch, CancellationToken cancellationToken) =>
        Task.FromResult(HeadSha);

    public Task<Stream> DownloadArchiveAsync(string accessToken, string owner, string repo, string commitSha, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref _archiveDownloads);
        if (ArchiveFailure(repo, attempt) is { } error)
        {
            throw error;
        }

        return Task.FromResult<Stream>(BuildArchive($"{owner}-{repo}-{commitSha[..7]}/"));
    }

    private static MemoryStream BuildArchive(string root)
    {
        var files = new Dictionary<string, string>
        {
            ["README.md"] = "# Shop\n\nA sample e-commerce API. Orders are created by the OrderService.\n",
            ["src/Api/Program.cs"] = "var builder = WebApplication.CreateBuilder(args);\nvar app = builder.Build();\napp.MapPost(\"/orders\", (OrderService orders) => orders.Create());\napp.Run();\n",
            ["src/Api/Orders/OrderService.cs"] = "namespace Shop.Orders;\n\npublic sealed class OrderService\n{\n    public Order Create() => new Order(Guid.NewGuid());\n}\n",
            ["src/Api/Orders/Order.cs"] = "namespace Shop.Orders;\n\npublic sealed record Order(Guid Id);\n",
            ["src/Api/appsettings.json"] = "{ \"ConnectionStrings\": { \"Db\": \"\" } }\n",
            ["src/Api/Api.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n",
            ["tests/Api.Tests/OrderServiceTests.cs"] = "public class OrderServiceTests { }\n",
            ["node_modules/left-pad/index.js"] = "module.exports = () => {};\n",
        };

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var entry = archive.CreateEntry(root + path).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        stream.Position = 0;
        return stream;
    }
}
