using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Options;

namespace RepoLens.Application.Source;

/// <summary>Reads a GitHub zipball into a <see cref="SourceSnapshot"/> of eligible text files.</summary>
public sealed class SourceSnapshotLoader(ICodeFileFilter filter, IOptions<SourceOptions> options)
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public SourceSnapshot Load(Stream zipArchive)
    {
        ArgumentNullException.ThrowIfNull(zipArchive);

        var limits = options.Value;
        var files = new List<SourceFile>();
        var allPaths = new List<string>();
        var skipped = 0;
        var truncated = false;
        long totalBytes = 0;

        using var archive = new ZipArchive(zipArchive, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var path = StripRootFolder(entry.FullName);
            if (path.Length == 0 || !IsSafeRelativePath(path))
            {
                continue;
            }

            if (allPaths.Count < limits.MaxTreePaths)
            {
                allPaths.Add(path);
            }

            if (truncated || !filter.IsEligible(path, entry.Length))
            {
                skipped++;
                continue;
            }

            if (files.Count >= limits.MaxFiles || totalBytes + entry.Length > limits.MaxTotalBytes)
            {
                truncated = true;
                skipped++;
                continue;
            }

            var content = ReadText(entry, limits.MaxFileBytes);
            if (content is null)
            {
                skipped++;
                continue;
            }

            var size = Encoding.UTF8.GetByteCount(content);
            totalBytes += size;
            files.Add(new SourceFile(
                path,
                SourceLanguages.Detect(path) ?? "text",
                FileClassifier.Classify(path),
                content,
                CountLines(content),
                size));
        }

        allPaths.Sort(StringComparer.Ordinal);
        files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return new SourceSnapshot(files, allPaths, skipped, truncated);
    }

    /// <summary>GitHub zipballs wrap everything in a single "{owner}-{repo}-{sha}/" folder.</summary>
    private static string StripRootFolder(string fullName)
    {
        var normalized = fullName.Replace('\\', '/');
        var slash = normalized.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

    private static bool IsSafeRelativePath(string path) =>
        !path.StartsWith('/') && !path.Split('/').Any(segment => segment is ".." or ".");

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> (the size in the zip header can lie, so the read is bounded).
    /// Returns null for binary or non-UTF-8 content.
    /// </summary>
    private static string? ReadText(ZipArchiveEntry entry, int maxBytes)
    {
        using var stream = entry.Open();
        var buffer = new byte[maxBytes + 1];
        var read = 0;
        int n;
        while (read < buffer.Length && (n = stream.Read(buffer, read, buffer.Length - read)) > 0)
        {
            read += n;
        }

        if (read == 0 || read > maxBytes)
        {
            return null;
        }

        var bytes = buffer.AsSpan(0, read);
        if (bytes.Slice(0, Math.Min(read, 8_000)).Contains((byte)0))
        {
            return null;
        }

        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }

        try
        {
            var text = StrictUtf8.GetString(bytes);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static int CountLines(string content)
    {
        var lines = 1;
        foreach (var c in content)
        {
            if (c == '\n')
            {
                lines++;
            }
        }

        return content.EndsWith('\n') ? lines - 1 : lines;
    }
}
