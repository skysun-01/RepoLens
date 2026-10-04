using EphemeralMongo;

[assembly: AssemblyFixture(typeof(RepoLens.IntegrationTests.Infrastructure.MongoFixture))]

namespace RepoLens.IntegrationTests.Infrastructure;

/// <summary>
/// One real MongoDB 8 server for the whole test run (no Docker needed): EphemeralMongo downloads the
/// official binaries once and starts mongod on a random port. Each test class uses its own database.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    private IMongoRunner? _runner;

    public string ConnectionString => _runner?.ConnectionString ?? throw new InvalidOperationException("MongoDB is not running.");

    public async ValueTask InitializeAsync()
    {
        _runner = await MongoRunner.RunAsync(new MongoRunnerOptions
        {
            Version = MongoVersion.V8,
            ConnectionTimeout = TimeSpan.FromMinutes(5),
        });
    }

    public ValueTask DisposeAsync()
    {
        _runner?.Dispose();
        return ValueTask.CompletedTask;
    }
}
