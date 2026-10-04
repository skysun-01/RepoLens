using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Common.Abstractions.Storage;

/// <summary>Stores generated files (the summary PDFs). MongoDB GridFS by default, Azure Blob Storage optionally.</summary>
public interface IFileStorage
{
    /// <param name="key">Logical path, for example <c>summaries/{userId}/{repoId}/{jobId}.pdf</c>.</param>
    Task<StoredFile> SaveAsync(string key, string fileName, string contentType, Stream content, CancellationToken cancellationToken);

    /// <summary>Opens the file for reading, or returns null if it no longer exists.</summary>
    Task<Stream?> OpenReadAsync(StoredFile file, CancellationToken cancellationToken);

    Task DeleteAsync(StoredFile file, CancellationToken cancellationToken);
}
