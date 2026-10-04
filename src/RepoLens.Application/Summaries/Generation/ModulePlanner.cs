using System.Collections.Frozen;
using Microsoft.Extensions.Options;
using RepoLens.Application.Source;

namespace RepoLens.Application.Summaries.Generation;

/// <summary>One module sent to the map phase: files with content, plus paths that did not fit the budget.</summary>
public sealed record ModuleBatch(string Name, IReadOnlyList<SourceFile> Files, IReadOnlyList<string> OtherPaths);

/// <summary>What the reduce phase receives besides the module digests.</summary>
public sealed record SummaryPlan(
    SourceFile? Readme,
    IReadOnlyList<SourceFile> KeyFiles,
    IReadOnlyList<ModuleBatch> Modules,
    bool SinglePass);

/// <summary>Groups files into modules and decides what fits into each model call.</summary>
public sealed class ModulePlanner(IOptions<SummaryGenerationOptions> options)
{
    public const string RootModule = "(root)";
    public const string OtherModule = "(other)";

    /// <summary>Folders that hold several modules; the module is the next folder down (for example src/Api).</summary>
    private static readonly FrozenSet<string> ContainerFolders = new[]
    {
        "src", "lib", "libs", "app", "apps", "packages", "services", "modules", "pkg", "internal", "cmd",
        "source", "components", "projects", "crates", "plugins", "backend", "frontend", "server", "client",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public SummaryPlan Plan(SourceSnapshot snapshot)
    {
        var budget = options.Value;
        var readme = snapshot.Files
            .Where(f => f.Kind == FileKind.Readme)
            .OrderBy(f => f.Path.Count(c => c == '/'))
            .ThenBy(f => f.Path.Length)
            .FirstOrDefault();

        var keyFiles = TakeWithinBudget(
            snapshot.Files
                .Where(f => f.Kind is FileKind.Manifest or FileKind.Infrastructure or FileKind.EntryPoint or FileKind.Configuration)
                .OrderBy(f => FileClassifier.Priority(f.Kind))
                .ThenBy(f => f.Path.Count(c => c == '/'))
                .ThenBy(f => f.SizeBytes),
            budget.MaxKeyFileChars);

        var moduleFiles = snapshot.Files
            .Where(f => f.Kind is FileKind.Source or FileKind.EntryPoint or FileKind.Configuration or FileKind.Test or FileKind.Documentation)
            .Where(f => !ReferenceEquals(f, readme))
            .ToList();

        var totalChars = moduleFiles.Sum(f => (long)f.Content.Length) + (readme?.Content.Length ?? 0);
        var singlePass = totalChars <= budget.SinglePassMaxChars;

        if (singlePass)
        {
            var everything = new ModuleBatch(RootModule, moduleFiles, []);
            return new SummaryPlan(readme, keyFiles, [everything], SinglePass: true);
        }

        var modules = moduleFiles
            .GroupBy(f => ModuleNameOf(f.Path), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(f => (long)f.Content.Length))
            .ToList();

        var kept = modules.Take(budget.MaxModules - 1).ToList();
        var rest = modules.Skip(budget.MaxModules - 1).SelectMany(g => g).ToList();

        var batches = kept.Select(g => CreateBatch(g.Key, g)).ToList();
        if (rest.Count > 0)
        {
            batches.Add(CreateBatch(OtherModule, rest));
        }

        return new SummaryPlan(readme, keyFiles, batches, SinglePass: false);
    }

    public static string ModuleNameOf(string path)
    {
        var segments = path.Split('/');
        if (segments.Length == 1)
        {
            return RootModule;
        }

        return segments.Length >= 3 && ContainerFolders.Contains(segments[0])
            ? $"{segments[0]}/{segments[1]}"
            : segments[0];
    }

    private ModuleBatch CreateBatch(string name, IEnumerable<SourceFile> files)
    {
        var ordered = files
            .OrderBy(f => f.Kind == FileKind.Test ? 1 : 0)
            .ThenBy(f => FileClassifier.Priority(f.Kind))
            .ThenBy(f => f.Path.Count(c => c == '/'))
            .ThenByDescending(f => f.SizeBytes)
            .ToList();

        var included = TakeWithinBudget(ordered, options.Value.MaxCharsPerModule);
        var includedPaths = included.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var otherPaths = ordered.Where(f => !includedPaths.Contains(f.Path)).Select(f => f.Path).ToList();
        return new ModuleBatch(name, included, otherPaths);
    }

    /// <summary>
    /// Takes files in order while they fit. A file that does not fit is truncated to the remaining
    /// budget when enough room is left, so large central files are still partly seen.
    /// </summary>
    private static List<SourceFile> TakeWithinBudget(IEnumerable<SourceFile> files, int maxChars)
    {
        const int MinUsefulExcerpt = 2_000;
        var result = new List<SourceFile>();
        var remaining = maxChars;

        foreach (var file in files)
        {
            if (remaining <= 0)
            {
                break;
            }

            if (file.Content.Length <= remaining)
            {
                result.Add(file);
                remaining -= file.Content.Length;
            }
            else if (remaining >= MinUsefulExcerpt)
            {
                result.Add(file with { Content = file.Content[..remaining] + "\n… (truncated)" });
                remaining = 0;
            }
        }

        return result;
    }
}
