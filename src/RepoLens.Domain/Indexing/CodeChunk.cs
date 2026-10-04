using System.Security.Cryptography;
using System.Text;

namespace RepoLens.Domain.Indexing;

/// <summary>
/// A slice of one source file at one commit, with its embedding vector, used to answer questions.
/// </summary>
public sealed class CodeChunk
{
    private CodeChunk()
    {
    }

    public CodeChunk(Guid repoId, string commitSha, string path, string language, int startLine, int endLine, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(startLine, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(endLine, startLine);

        Id = CreateId(repoId, commitSha, path, startLine);
        RepoId = repoId;
        CommitSha = commitSha;
        Path = path;
        Language = language;
        StartLine = startLine;
        EndLine = endLine;
        Content = content;
    }

    /// <summary>Deterministic, so re-indexing the same commit after a retry overwrites instead of duplicating.</summary>
    public string Id { get; private set; } = string.Empty;

    public Guid RepoId { get; private set; }

    public string CommitSha { get; private set; } = string.Empty;

    public string Path { get; private set; } = string.Empty;

    public string Language { get; private set; } = string.Empty;

    public int StartLine { get; private set; }

    public int EndLine { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public float[] Embedding { get; private set; } = [];

    public void AttachEmbedding(float[] embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length == 0)
        {
            throw new ArgumentException("Embedding must not be empty.", nameof(embedding));
        }

        Embedding = embedding;
    }

    public static string CreateId(Guid repoId, string commitSha, string path, int startLine)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{repoId:N}|{commitSha}|{path}|{startLine}"));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }
}
