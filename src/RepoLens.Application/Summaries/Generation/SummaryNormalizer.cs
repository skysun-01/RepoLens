using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries.Generation;

/// <summary>
/// Makes model output safe to store and render: no nulls, trimmed text, and bounded list sizes so a
/// runaway answer cannot produce a 300-page PDF.
/// </summary>
public static class SummaryNormalizer
{
    private const int MaxText = 4_000;
    private const int MaxShortText = 600;
    private const int MaxItems = 30;
    private const int MaxSteps = 15;

    public static RepoSummary Normalize(RepoSummary? summary)
    {
        if (summary is null)
        {
            return new RepoSummary();
        }

        return summary with
        {
            Overview = Text(summary.Overview, MaxText),
            Audience = Text(summary.Audience, MaxText),
            Architecture = Text(summary.Architecture, MaxText * 2),
            DataModel = Text(summary.DataModel, MaxText),
            TechStack = List(summary.TechStack, t => t with { Name = Text(t.Name, 120), Category = Text(t.Category, 40), Purpose = Text(t.Purpose, MaxShortText) }, t => t.Name),
            Modules = List(summary.Modules, m => m with
            {
                Path = Text(m.Path, 300),
                Responsibility = Text(m.Responsibility, MaxShortText * 2),
                KeyFiles = Strings(m.KeyFiles, 12, 300),
            }, m => m.Path),
            KeyFlows = List(summary.KeyFlows, f => f with
            {
                Name = Text(f.Name, 160),
                Summary = Text(f.Summary, MaxShortText),
                Steps = Strings(f.Steps, MaxSteps, MaxShortText),
            }, f => f.Name, maxItems: 10),
            EntryPoints = List(summary.EntryPoints, e => e with { Path = Text(e.Path, 300), Description = Text(e.Description, MaxShortText) }, e => e.Path),
            Configuration = List(summary.Configuration, c => c with { Name = Text(c.Name, 200), Description = Text(c.Description, MaxShortText) }, c => c.Name),
            BuildAndRun = List(summary.BuildAndRun, s => s with { Title = Text(s.Title, 300), Command = Text(s.Command, MaxShortText) }, s => s.Title),
            WhereToStart = Strings(summary.WhereToStart, 12, MaxShortText),
            Glossary = List(summary.Glossary, g => g with { Term = Text(g.Term, 120), Definition = Text(g.Definition, MaxShortText) }, g => g.Term),
        };
    }

    private static string Text(string? value, int max)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= max ? trimmed : string.Concat(trimmed.AsSpan(0, max - 1), "…");
    }

    private static IReadOnlyList<string> Strings(IReadOnlyList<string>? values, int maxItems, int maxLength) =>
        (values ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Text(v, maxLength))
            .Take(maxItems)
            .ToList();

    private static IReadOnlyList<T> List<T>(IReadOnlyList<T>? values, Func<T, T> clean, Func<T, string> key, int maxItems = MaxItems)
        where T : class =>
        (values ?? [])
            .Where(v => v is not null)
            .Select(clean)
            .Where(v => !string.IsNullOrWhiteSpace(key(v)))
            .Take(maxItems)
            .ToList();
}
