using System.Collections.Frozen;

namespace RepoLens.Application.Source;

/// <summary>Decides which files in a repository are worth reading.</summary>
public interface ICodeFileFilter
{
    /// <summary>True when the file should be read, given its repository-relative path and size.</summary>
    bool IsEligible(string path, long sizeBytes);
}

/// <summary>
/// Skips dependencies, build output, lock files, minified bundles, secrets and anything that is not
/// source or text. Folder and file lists follow the reference implementation from the video, extended
/// for .NET and a few more ecosystems.
/// </summary>
public sealed class DefaultCodeFileFilter(Microsoft.Extensions.Options.IOptions<SourceOptions> options) : ICodeFileFilter
{
    private static readonly FrozenSet<string> SkippedFolders = new[]
    {
        "node_modules", "bower_components", "jspm_packages", ".git", ".hg", ".svn",
        "dist", "build", "out", "target", "bin", "obj", ".next", ".nuxt", ".svelte-kit", ".turbo", ".output",
        "vendor", "third_party", "__pycache__", ".pytest_cache", ".mypy_cache", ".tox", "venv", ".venv", "env",
        ".idea", ".vscode", ".vs", ".gradle", "coverage", ".nyc_output", "pods", "deriveddata",
        ".terraform", ".serverless", ".angular", ".cache", "tmp", "temp", "logs",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> SkippedFileNames = new[]
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "npm-shrinkwrap.json", "bun.lockb", "bun.lock",
        "composer.lock", "cargo.lock", "poetry.lock", "pipfile.lock", "gemfile.lock", "go.sum", "packages.lock.json",
        "mix.lock", "pubspec.lock", "podfile.lock", "flake.lock", "deno.lock",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dot-files that are useful despite the leading dot.</summary>
    private static readonly FrozenSet<string> AllowedDotFiles = new[]
    {
        ".env.example", ".env.sample", ".gitlab-ci.yml",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dot-folders that are useful despite the leading dot.</summary>
    private static readonly FrozenSet<string> AllowedDotFolders = new[]
    {
        ".github", ".devcontainer", ".circleci", ".azure", ".config",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public bool IsEligible(string path, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(path) || sizeBytes <= 0 || sizeBytes > options.Value.MaxFileBytes)
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var folder = segments[i];
            if (SkippedFolders.Contains(folder) || (folder.StartsWith('.') && !AllowedDotFolders.Contains(folder)))
            {
                return false;
            }
        }

        var fileName = segments[^1];
        if (SkippedFileNames.Contains(fileName))
        {
            return false;
        }

        if (fileName.StartsWith('.') && !AllowedDotFiles.Contains(fileName))
        {
            return false;
        }

        if (IsMinifiedOrGenerated(fileName))
        {
            return false;
        }

        return SourceLanguages.Detect(path) is not null;
    }

    private static bool IsMinifiedOrGenerated(string fileName) =>
        fileName.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".pb.go", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith("_pb2.py", StringComparison.OrdinalIgnoreCase);
}
