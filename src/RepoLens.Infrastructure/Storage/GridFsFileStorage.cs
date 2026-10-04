using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using RepoLens.Application.Common.Abstractions.Storage;
using RepoLens.Domain.Summaries;
using RepoLens.Infrastructure.Persistence;

namespace RepoLens.Infrastructure.Storage;

public sealed class GridFsOptions
{
    public const string SectionName = "Storage:GridFs";

    public string BucketName { get; set; } = "summary_pdfs";
}

/// <summary>Stores files in MongoDB GridFS, so PDFs live in the same database as everything else.</summary>
internal sealed class GridFsFileStorage(MongoContext context, IOptions<GridFsOptions> options) : IFileStorage
{
    private readonly GridFSBucket _bucket = new(context.Database, new GridFSBucketOptions { BucketName = options.Value.BucketName });

    public async Task<StoredFile> SaveAsync(string key, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var uploadOptions = new GridFSUploadOptions
        {
            Metadata = new BsonDocument
            {
                { "fileName", fileName },
                { "contentType", contentType },
            },
        };

        var id = await _bucket.UploadFromStreamAsync(key, content, uploadOptions, cancellationToken);
        var info = await _bucket.Find(Builders<GridFSFileInfo>.Filter.Eq("_id", id)).FirstOrDefaultAsync(cancellationToken);
        return new StoredFile(id.ToString(), fileName, contentType, info?.Length ?? content.Length);
    }

    public async Task<Stream?> OpenReadAsync(StoredFile file, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(file.StorageKey, out var id))
        {
            return null;
        }

        try
        {
            return await _bucket.OpenDownloadStreamAsync(id, new GridFSDownloadOptions { Seekable = true }, cancellationToken);
        }
        catch (GridFSFileNotFoundException)
        {
            return null;
        }
    }

    public async Task DeleteAsync(StoredFile file, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(file.StorageKey, out var id))
        {
            return;
        }

        try
        {
            await _bucket.DeleteAsync(id, cancellationToken);
        }
        catch (GridFSFileNotFoundException)
        {
            // Already gone.
        }
    }
}
