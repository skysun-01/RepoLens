using RepoLens.Application;
using RepoLens.Application.Summaries;
using RepoLens.Infrastructure;
using RepoLens.Infrastructure.Hosting;
using RepoLens.Infrastructure.Messaging;

// The Worker consumes GenerateSummaryCommand from Azure Service Bus and turns repositories into PDFs.
// It has no HTTP endpoints; Azure Container Apps scales it on the queue length (KEDA).
var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddRepoLensKeyVault();

if (builder.Configuration.GetMessagingProvider() != MessagingProvider.ServiceBus)
{
    throw new InvalidOperationException(
        "The Worker reads from Azure Service Bus, so it needs Messaging:Provider = ServiceBus. " +
        "With the in-memory provider the API processes summaries itself; there is no need to run the Worker.");
}

builder.Services.AddRepoLensTelemetry(builder.Configuration, serviceName: "repolens-worker");
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSummaryProcessing();
builder.Services.AddMessageConsumer<GenerateSummaryCommand>(builder.Configuration);

// Give in-flight jobs time to hand their message back before the process exits.
builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(60));

builder.Build().Run();
