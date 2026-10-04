using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Abstractions.Search;
using RepoLens.Application.Source;
using RepoLens.Domain.Indexing;
using RepoLens.Domain.Repos;

namespace RepoLens.Application.Indexing;

/// <summary>Chunks a snapshot, embeds the chunks and stores them for question answering.</summary>
public sealed partial class CodeIndexer(
    CodeChunker chunker,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ICodeChunkStore chunkStore,
    IRepoIndexRepository indexes,
    IOptions<IndexingOptions> options,
    TimeProvider timeProvider,
    ILogger<CodeIndexer> logger)
{
    /// <param name="reportProgress">Receives the completed fraction, 0 to 1.</param>
    public async Task<RepoIndex> IndexAsync(
        Repo repo,
        string commitSha,
        SourceSnapshot snapshot,
        Func<double, CancellationToken, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        var modelId = EmbeddingModel.IdOf(embeddingGenerator);
        var existing = await indexes.GetAsync(repo.Id, cancellationToken);

        if (existing is not null
            && existing.CommitSha == commitSha
            && existing.EmbeddingModel == modelId
            && await chunkStore.CountAsync(repo.Id, commitSha, cancellationToken) == existing.ChunkCount)
        {
            LogAlreadyIndexed(repo.Id, commitSha);
            return existing;
        }

        var chunks = snapshot.Files
            .OrderBy(f => FileClassifier.Priority(f.Kind))
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .SelectMany(f => chunker.Chunk(repo.Id, commitSha, f))
            .Take(options.Value.MaxChunks)
            .ToList();

        var batchSize = options.Value.EmbeddingBatchSize;
        for (var offset = 0; offset < chunks.Count; offset += batchSize)
        {
            var batch = chunks.Skip(offset).Take(batchSize).ToList();
            await EmbedAsync(batch, cancellationToken);
            await chunkStore.UpsertAsync(batch, cancellationToken);
            await reportProgress((offset + batch.Count) / (double)chunks.Count, cancellationToken);
        }

        await chunkStore.DeleteAsync(repo.Id, keepCommitSha: commitSha, cancellationToken);

        var fileCount = chunks.Select(c => c.Path).Distinct(StringComparer.Ordinal).Count();
        var index = new RepoIndex(repo.Id, repo.UserId, commitSha, chunks.Count, fileCount, modelId, timeProvider.GetUtcNow());
        await indexes.UpsertAsync(index, cancellationToken);

        LogIndexed(repo.Id, commitSha, chunks.Count, fileCount);
        return index;
    }

    private async Task EmbedAsync(List<CodeChunk> batch, CancellationToken cancellationToken)
    {
        var embeddings = await embeddingGenerator.GenerateAsync(
            batch.Select(CodeChunker.EmbeddingText),
            cancellationToken: cancellationToken);

        if (embeddings.Count != batch.Count)
        {
            throw new InvalidOperationException($"The embedding model returned {embeddings.Count} vectors for {batch.Count} inputs.");
        }

        for (var i = 0; i < batch.Count; i++)
        {
            batch[i].AttachEmbedding(embeddings[i].Vector.ToArray());
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Repo {RepoId} is already indexed at {CommitSha}")]
    private partial void LogAlreadyIndexed(Guid repoId, string commitSha);

    [LoggerMessage(Level = LogLevel.Information, Message = "Indexed repo {RepoId} at {CommitSha}: {ChunkCount} chunks from {FileCount} files")]
    private partial void LogIndexed(Guid repoId, string commitSha, int chunkCount, int fileCount);
}
