namespace RepoLens.Application.Source;

/// <summary>What a file is for; drives which files the summary reads first.</summary>
public enum FileKind
{
    Readme,
    Documentation,
    Manifest,
    EntryPoint,
    Configuration,
    Infrastructure,
    Source,
    Test,
    Other,
}

public sealed record SourceFile(string Path, string Language, FileKind Kind, string Content, int LineCount, long SizeBytes)
{
    /// <summary>Top-level folder, or empty for files at the repository root.</summary>
    public string TopFolder => Path.Contains('/', StringComparison.Ordinal) ? Path[..Path.IndexOf('/', StringComparison.Ordinal)] : string.Empty;
}

/// <summary>The readable text files of a repository at one commit, plus the full list of paths.</summary>
public sealed class SourceSnapshot(IReadOnlyList<SourceFile> files, IReadOnlyList<string> allPaths, int skippedFiles, bool truncated)
{
    public IReadOnlyList<SourceFile> Files { get; } = files;

    /// <summary>Every file path in the archive, including skipped ones, capped by <see cref="SourceOptions.MaxTreePaths"/>.</summary>
    public IReadOnlyList<string> AllPaths { get; } = allPaths;

    public int SkippedFiles { get; } = skippedFiles;

    /// <summary>True when a size or count limit stopped reading before the end of the archive.</summary>
    public bool Truncated { get; } = truncated;

    public long TotalBytes => Files.Sum(f => f.SizeBytes);

    /// <summary>The most common language among source files, for the PDF cover.</summary>
    public string? PrimaryLanguage => Files
        .Where(f => f.Kind is FileKind.Source or FileKind.EntryPoint)
        .GroupBy(f => f.Language)
        .OrderByDescending(g => g.Sum(f => f.SizeBytes))
        .Select(g => g.Key)
        .FirstOrDefault();
}
