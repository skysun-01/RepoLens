using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RepoLens.Infrastructure.AI;

namespace RepoLens.Infrastructure.Hosting;

public static class HostingExtensions
{
    /// <summary>
    /// Loads secrets from Azure Key Vault when <c>KeyVault:Uri</c> is set, using managed identity.
    /// Secret names map to settings with <c>--</c> as the separator, for example <c>Auth--Jwt--SigningKey</c>.
    /// </summary>
    public static IConfigurationManager AddRepoLensKeyVault(this IConfigurationManager configuration)
    {
        var vaultUri = configuration["KeyVault:Uri"];
        if (!string.IsNullOrWhiteSpace(vaultUri))
        {
            configuration.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential());
        }

        return configuration;
    }

    /// <summary>
    /// OpenTelemetry traces and metrics for outgoing HTTP, Azure SDK calls (Service Bus) and AI calls.
    /// Exported to Application Insights when <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> is set.
    /// </summary>
    public static IServiceCollection AddRepoLensTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        // Azure SDK clients emit ActivitySource spans only when this switch is on.
        AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddSource("Azure.*", AiServiceCollectionExtensions.TelemetrySourceName, "Experimental.Microsoft.Extensions.AI")
                    .AddHttpClientInstrumentation();
                configureTracing?.Invoke(tracing);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddHttpClientInstrumentation()
                    .AddMeter(AiServiceCollectionExtensions.TelemetrySourceName, "Experimental.Microsoft.Extensions.AI");
                configureMetrics?.Invoke(metrics);
            });

        if (!string.IsNullOrWhiteSpace(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            telemetry.UseAzureMonitorExporter();
        }

        return services;
    }
}
