namespace RepoLens.Domain.Summaries;

/// <summary>A file saved in file storage (GridFS or Blob). <see cref="StorageKey"/> is provider specific.</summary>
public sealed record StoredFile(string StorageKey, string FileName, string ContentType, long SizeBytes);
