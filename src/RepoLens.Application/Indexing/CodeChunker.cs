using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RepoLens.Application.Source;
using RepoLens.Domain.Indexing;

namespace RepoLens.Application.Indexing;

/// <summary>
/// Splits a file into line-aligned chunks of roughly <see cref="IndexingOptions.MaxChunkChars"/>.
/// A chunk ends early at a natural code boundary (class, function, type) once it has at least 60% of
/// the target size, and the next chunk repeats a small overlap. Line numbers are exact and 1-based.
/// </summary>
public sealed partial class CodeChunker(IOptions<IndexingOptions> options)
{
    private const double BoundaryFillRatio = 0.6;

    public IReadOnlyList<CodeChunk> Chunk(Guid repoId, string commitSha, SourceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var lines = ParseLines(file.Content);
        if (lines.Count == 0)
        {
            return [];
        }

        var chunkSize = options.Value.MaxChunkChars;
        var overlap = Math.Min(options.Value.OverlapChars, chunkSize / 2);
        var boundary = BoundaryPattern(file.Language);
        var chunks = new List<CodeChunk>();
        var start = 0;

        while (start < lines.Count)
        {
            var end = start;
            var length = lines[start].Length;

            while (end + 1 < lines.Count)
            {
                var next = lines[end + 1];
                var nextLength = next.Length + 1;

                if (end > start && length >= chunkSize * BoundaryFillRatio && IsBoundary(boundary, next))
                {
                    break;
                }

                if (end > start && length + nextLength > chunkSize)
                {
                    break;
                }

                length += nextLength;
                end++;
            }

            var content = Join(lines, start, end);
            if (!string.IsNullOrWhiteSpace(content))
            {
                chunks.Add(new CodeChunk(repoId, commitSha, file.Path, file.Language, start + 1, end + 1, content));
            }

            if (end >= lines.Count - 1)
            {
                break;
            }

            start = NextStart(lines, start, end, overlap);
        }

        return chunks;
    }

    /// <summary>The text that gets embedded: the location first, so path words help retrieval too.</summary>
    public static string EmbeddingText(CodeChunk chunk) =>
        $"File: {chunk.Path} (lines {chunk.StartLine}-{chunk.EndLine})\n{chunk.Content}";

    internal static List<string> ParseLines(string content)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(content))
        {
            return lines;
        }

        var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c is not ('\r' or '\n'))
            {
                continue;
            }

            lines.Add(content[start..i]);
            if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        if (start < content.Length)
        {
            lines.Add(content[start..]);
        }

        return lines;
    }

    private static int NextStart(List<string> lines, int start, int end, int overlap)
    {
        var next = end + 1;
        if (overlap <= 0)
        {
            return next;
        }

        var accumulated = 0;
        var candidate = end;
        while (candidate > start)
        {
            accumulated += lines[candidate].Length + 1;
            if (accumulated >= overlap)
            {
                break;
            }

            candidate--;
        }

        return Math.Max(start + 1, candidate);
    }

    private static string Join(List<string> lines, int start, int end)
    {
        var builder = new StringBuilder();
        for (var i = start; i <= end; i++)
        {
            if (i > start)
            {
                builder.Append('\n');
            }

            builder.Append(lines[i]);
        }

        return builder.ToString();
    }

    private static bool IsBoundary(Regex? pattern, string line)
    {
        if (pattern is null || line.Length is 0 or > 500)
        {
            return false;
        }

        try
        {
            return pattern.IsMatch(line);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static Regex? BoundaryPattern(string language) => language switch
    {
        "csharp" or "java" or "kotlin" or "scala" or "fsharp" => JvmBoundary(),
        "python" => PythonBoundary(),
        "typescript" or "javascript" or "vue" or "svelte" => JavaScriptBoundary(),
        "go" => GoBoundary(),
        "rust" => RustBoundary(),
        "c" or "cpp" or "objectivec" => CBoundary(),
        _ => null,
    };

    [GeneratedRegex(@"^\s*(public|protected|private|internal|abstract|final|sealed|static|override|default|partial)?\s*(class|interface|enum|record|object|data\s+class|struct|fun|def|void|[A-Z]\w*)\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex JvmBoundary();

    [GeneratedRegex(@"^\s{0,4}(class|def|async\s+def)\s+\w+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PythonBoundary();

    [GeneratedRegex(@"^\s*(export\s+)?(default\s+)?(async\s+)?(function|class|interface|type|const|let|var)\s+\w+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex JavaScriptBoundary();

    [GeneratedRegex(@"^(func|type|var|const)\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex GoBoundary();

    [GeneratedRegex(@"^\s*(pub(\([^)]+\))?\s+)?(fn|struct|enum|trait|impl|type|const|static)\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex RustBoundary();

    [GeneratedRegex(@"^\s*(class|struct|enum|union|namespace|template)\b|^[a-zA-Z_]\w*\s+[a-zA-Z_]\w*\s*\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex CBoundary();
}
