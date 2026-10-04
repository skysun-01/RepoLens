using System.ComponentModel.DataAnnotations;

namespace RepoLens.Application.Source;

/// <summary>Limits applied when reading a repository archive.</summary>
public sealed class SourceOptions
{
    public const string SectionName = "Source";

    /// <summary>Files larger than this are skipped (generated code, data dumps, minified bundles).</summary>
    [Range(1_024, 5_242_880)]
    public int MaxFileBytes { get; set; } = 150_000;

    [Range(1, 50_000)]
    public int MaxFiles { get; set; } = 5_000;

    [Range(1_024, 1_073_741_824)]
    public long MaxTotalBytes { get; set; } = 60_000_000;

    /// <summary>How many paths of the full file tree to keep for the summary (including skipped files).</summary>
    [Range(100, 100_000)]
    public int MaxTreePaths { get; set; } = 5_000;
}
