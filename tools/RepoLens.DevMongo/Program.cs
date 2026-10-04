using EphemeralMongo;

// Local MongoDB for development, with no Docker and no installer. It downloads the official MongoDB 8
// Community binaries once, then runs mongod on localhost:27017 with data kept in <repo>/.data/mongo.
//
//   dotnet run --project tools/RepoLens.DevMongo            (port 27017)
//   dotnet run --project tools/RepoLens.DevMongo -- 27018   (another port)
//
// Stop it with Ctrl+C. Data survives restarts; delete .data/mongo to start fresh.

var port = args.Length > 0 && int.TryParse(args[0], out var customPort) ? customPort : 27017;
var dataDirectory = Path.Combine(FindRepositoryRoot(), ".data", "mongo");
Directory.CreateDirectory(dataDirectory);

Console.WriteLine($"Starting MongoDB on port {port} (data: {dataDirectory}) ...");
Console.WriteLine("The first run downloads MongoDB (about 100 MB); later runs start in seconds.");

var options = new MongoRunnerOptions
{
    Version = MongoVersion.V8,
    MongoPort = port,
    DataDirectory = dataDirectory,
    ConnectionTimeout = TimeSpan.FromMinutes(5),
    StandardErrorLogger = line => Console.Error.WriteLine($"[mongod] {line}"),
};

using var runner = await MongoRunner.RunAsync(options);

Console.WriteLine();
Console.WriteLine($"MongoDB is ready: {runner.ConnectionString}");
Console.WriteLine("Press Ctrl+C to stop.");

var stopped = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopped.TrySetResult();
};
await stopped.Task;

Console.WriteLine("Stopping MongoDB ...");
return;

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RepoLens.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? Directory.GetCurrentDirectory();
}
