using System.Text.RegularExpressions;
using RepoLens.Application.Common.Abstractions.Search;
using RepoLens.Domain.Conversations;
using RepoLens.Domain.Repos;

namespace RepoLens.Application.Questions;

/// <summary>Turns the [n] markers in an answer into citations that link to the exact lines on GitHub.</summary>
public static partial class CitationMapper
{
    public static IReadOnlyList<Citation> FromAnswer(string answer, Repo repo, string commitSha, IReadOnlyList<ScoredChunk> sources)
    {
        var citations = new List<Citation>();
        var seen = new HashSet<int>();

        foreach (Match match in SourceMarker().Matches(answer))
        {
            foreach (var part in match.Groups["ids"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var id) && id >= 1 && id <= sources.Count && seen.Add(id))
                {
                    citations.Add(ToCitation(repo, commitSha, sources[id - 1]));
                }
            }
        }

        return citations;
    }

    public static Citation ToCitation(Repo repo, string commitSha, ScoredChunk source)
    {
        var chunk = source.Chunk;
        var encodedPath = string.Join('/', chunk.Path.Split('/').Select(Uri.EscapeDataString));
        var url = $"{repo.HtmlUrl.TrimEnd('/')}/blob/{commitSha}/{encodedPath}#L{chunk.StartLine}-L{chunk.EndLine}";
        return new Citation(chunk.Path, chunk.StartLine, chunk.EndLine, url);
    }

    [GeneratedRegex(@"\[(?<ids>\d{1,3}(?:\s*,\s*\d{1,3})*)\]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex SourceMarker();
}
