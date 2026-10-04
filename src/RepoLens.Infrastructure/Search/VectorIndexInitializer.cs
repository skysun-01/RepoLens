using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoLens.Infrastructure.Persistence;

namespace RepoLens.Infrastructure.Search;

/// <summary>Creates the vector index for the configured provider. Failures are logged, not fatal: search reports them per query.</summary>
internal sealed partial class VectorIndexInitializer(
    MongoContext context,
    IOptions<VectorSearchOptions> options,
    ILogger<VectorIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.CreateIndexIfMissing || settings.Provider == VectorSearchProvider.InApp)
        {
            return;
        }

        try
        {
            if (settings.Provider == VectorSearchProvider.Atlas)
            {
                await AtlasVectorSearch.EnsureIndexAsync(context, settings, cancellationToken);
            }
            else
            {
                await CosmosVectorSearch.EnsureIndexAsync(context, settings, cancellationToken);
            }

            LogReady(settings.Provider, settings.IndexName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex, settings.Provider, settings.IndexName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "{Provider} vector index {IndexName} is ready")]
    private partial void LogReady(VectorSearchProvider provider, string indexName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not create the {Provider} vector index {IndexName}; create it manually (see README)")]
    private partial void LogFailed(Exception exception, VectorSearchProvider provider, string indexName);
}
