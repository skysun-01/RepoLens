using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace RepoLens.Api.RateLimiting;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 100_000)]
    public int GlobalPerMinute { get; set; } = 300;

    [Range(1, 10_000)]
    public int SignInPerMinute { get; set; } = 20;

    [Range(1, 10_000)]
    public int SummariesPerHour { get; set; } = 30;

    [Range(1, 10_000)]
    public int QuestionsPerMinute { get; set; } = 20;

    [Range(1, 10_000)]
    public int SyncsPerHour { get; set; } = 30;
}

/// <summary>Per-user limits (per IP before sign-in) protecting GitHub quota and AI spend.</summary>
public static class RateLimitingSetup
{
    public const string SignIn = "sign-in";
    public const string Summaries = "summaries";
    public const string Questions = "questions";
    public const string Sync = "sync";

    public static IServiceCollection AddRepoLensRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests.",
                    Detail = "You are sending requests faster than allowed. Wait a moment and try again.",
                    Extensions = { ["code"] = "rate_limited" },
                };
                await context.HttpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
            };

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => Window(Limits(context).GlobalPerMinute, TimeSpan.FromMinutes(1))));

            limiter.AddPolicy(SignIn, context =>
                RateLimitPartition.GetFixedWindowLimiter(IpKey(context), _ => Window(Limits(context).SignInPerMinute, TimeSpan.FromMinutes(1))));

            limiter.AddPolicy(Summaries, context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => Window(Limits(context).SummariesPerHour, TimeSpan.FromHours(1))));

            limiter.AddPolicy(Questions, context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => Window(Limits(context).QuestionsPerMinute, TimeSpan.FromMinutes(1))));

            limiter.AddPolicy(Sync, context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => Window(Limits(context).SyncsPerHour, TimeSpan.FromHours(1))));
        });

        return services;
    }

    private static RateLimitOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    private static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) => new()
    {
        PermitLimit = permits,
        Window = window,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value is { } userId ? $"user:{userId}" : IpKey(context);

    private static string IpKey(HttpContext context) => $"ip:{context.Connection.RemoteIpAddress}";
}
