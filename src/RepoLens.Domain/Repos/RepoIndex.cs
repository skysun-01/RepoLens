namespace RepoLens.Domain.Repos;

/// <summary>
/// Search-index state of one repository: which commit its code chunks were built from.
/// Keyed by the repository id; written only by the indexing step.
/// </summary>
public sealed class RepoIndex
{
    private RepoIndex()
    {
    }

    public RepoIndex(Guid repoId, Guid userId, string commitSha, int chunkCount, int fileCount, string embeddingModel, DateTimeOffset indexedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkCount);

        RepoId = repoId;
        UserId = userId;
        CommitSha = commitSha;
        ChunkCount = chunkCount;
        FileCount = fileCount;
        EmbeddingModel = embeddingModel;
        IndexedAt = indexedAt;
    }

    public Guid RepoId { get; private set; }

    public Guid UserId { get; private set; }

    public string CommitSha { get; private set; } = string.Empty;

    public int ChunkCount { get; private set; }

    public int FileCount { get; private set; }

    public string EmbeddingModel { get; private set; } = string.Empty;

    public DateTimeOffset IndexedAt { get; private set; }
}
