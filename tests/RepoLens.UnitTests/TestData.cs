using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Options;
using RepoLens.Application.Source;
using RepoLens.Domain.Repos;
using RepoLens.Domain.Summaries;

namespace RepoLens.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    public const string Sha = "0123456789abcdef0123456789abcdef01234567";

    public static RepoDetails Details(long id = 42, string name = "shop", string owner = "octo") => new(
        id, owner, name, $"{owner}/{name}", "A sample shop", "C#", IsPrivate: false, IsFork: false, IsArchived: false,
        "main", $"https://github.com/{owner}/{name}", Stars: 5, SizeKb: 120, PushedAt: Now.AddDays(-1));

    public static Repo Repo(Guid? userId = null) => global::RepoLens.Domain.Repos.Repo.Create(userId ?? Guid.NewGuid(), Details(), Now);

    public static SummaryJob Job(Guid? userId = null, Guid? repoId = null, string? sha = Sha) =>
        SummaryJob.Create(userId ?? Guid.NewGuid(), repoId ?? Guid.NewGuid(), "octo/shop", "main", sha, force: false, Now);

    public static IOptions<T> Options<T>(T value)
        where T : class => Microsoft.Extensions.Options.Options.Create(value);

    public static SourceFile File(string path, string content, FileKind? kind = null) =>
        new(path, SourceLanguages.Detect(path) ?? "text", kind ?? FileClassifier.Classify(path), content, content.Split('\n').Length, Encoding.UTF8.GetByteCount(content));

    /// <summary>A zip shaped like a GitHub zipball: everything under one "owner-repo-sha/" folder.</summary>
    public static MemoryStream Zip(IReadOnlyDictionary<string, byte[]> files, string root = "octo-shop-0123456/")
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry(root);
            foreach (var (path, content) in files)
            {
                var entry = archive.CreateEntry(root + path);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
