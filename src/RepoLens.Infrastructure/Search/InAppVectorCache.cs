using Microsoft.Extensions.Caching.Memory;

namespace RepoLens.Infrastructure.Search;

/// <summary>
/// A dedicated, size-limited cache for <see cref="InAppVectorSearch"/>. Size is counted in vectors:
/// 50,000 vectors of 1,536 dimensions is roughly 300 MB.
/// </summary>
internal sealed class InAppVectorCache : IDisposable
{
    public const long MaxVectors = 50_000;

    public MemoryCache Cache { get; } = new(new MemoryCacheOptions { SizeLimit = MaxVectors });

    public void Dispose() => Cache.Dispose();
}
