using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RepoLens.Api;
using RepoLens.Api.Auth;
using RepoLens.Api.ErrorHandling;
using RepoLens.Api.OpenApi;
using RepoLens.Api.RateLimiting;
using RepoLens.Application;
using RepoLens.Application.Common.Abstractions;
using RepoLens.Application.Summaries;
using RepoLens.Infrastructure;
using RepoLens.Infrastructure.Hosting;
using RepoLens.Infrastructure.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddRepoLensKeyVault();
builder.Services.AddRepoLensTelemetry(
    builder.Configuration,
    serviceName: "repolens-api",
    configureTracing: tracing => tracing.AddAspNetCoreInstrumentation(),
    configureMetrics: metrics => metrics.AddAspNetCoreInstrumentation());

// Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAccessTokens();
builder.Services.AddSingleton<IResourceLinks, ApiResourceLinks>();
builder.Services.AddSummaryOutbox();

// With the in-memory bus there is no separate Worker: the API processes summaries itself.
if (builder.Configuration.GetMessagingProvider() == MessagingProvider.InMemory)
{
    builder.Services.AddSummaryProcessing();
    builder.Services.AddMessageConsumer<GenerateSummaryCommand>(builder.Configuration);
}

// HTTP
builder.Services.AddRepoLensAuthentication();
builder.Services.AddRepoLensRateLimiting();
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Frontend:AllowedOrigins").Get<string[]>() ?? [];
    policy.WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("Location", "Content-Disposition", "Retry-After", "ETag");
}));

builder.Services.AddControllers()
    .AddJsonOptions(json => json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddOpenApi(openApi => openApi.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

// Azure Container Apps terminates TLS at its ingress and forwards the original scheme.
builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

var swaggerEnabled = app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled");
if (swaggerEnabled)
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(swagger =>
    {
        swagger.SwaggerEndpoint("/openapi/v1.json", "RepoLens API v1");
        swagger.RoutePrefix = "swagger";
        swagger.DocumentTitle = "RepoLens API";
        swagger.EnablePersistAuthorization();
        swagger.DisplayRequestDuration();
    });
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription().AllowAnonymous();
}

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous().DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous().DisableRateLimiting();

app.Run();

/// <summary>Entry point; public so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
