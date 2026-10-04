using System.ComponentModel;

namespace RepoLens.Application.Summaries.Generation;

/// <summary>Map-phase output: what the model learned about one module of the repository.</summary>
public sealed record ModuleDigest
{
    [Description("What this module is responsible for, in one to three sentences.")]
    public string Purpose { get; init; } = string.Empty;

    [Description("The most important files and their role, each written as 'path: role'.")]
    public IReadOnlyList<string> KeyFiles { get; init; } = [];

    [Description("Notable behaviours or flows handled here, each as one sentence that names the files involved.")]
    public IReadOnlyList<string> Flows { get; init; } = [];

    [Description("Frameworks, libraries and external services this module uses.")]
    public IReadOnlyList<string> Technologies { get; init; } = [];

    [Description("Configuration keys or environment variables this module reads.")]
    public IReadOnlyList<string> Configuration { get; init; } = [];

    [Description("Entities, data structures, tables or documents defined here.")]
    public IReadOnlyList<string> DataModel { get; init; } = [];
}
