using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Application.Common.Errors;
using RepoLens.Domain.Common;

namespace RepoLens.Api.ErrorHandling;

/// <summary>
/// Turns exceptions into RFC 9457 Problem Details. Expected failures keep their stable <c>code</c>
/// (for example <c>repo_not_indexed</c>) so clients can react; unexpected ones return a generic 500
/// without internal details.
/// </summary>
internal sealed partial class AppExceptionHandler(IProblemDetailsService problemDetails, ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
            return true;
        }

        var problem = exception switch
        {
            ValidationFailedException validation => new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The request is invalid.",
                Detail = validation.Message,
            },
            AppException app => new ProblemDetails
            {
                Status = StatusFor(app.Kind),
                Title = TitleFor(app.Kind),
                Detail = app.Message,
            },
            DomainException domain => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The request conflicts with the current state.",
                Detail = domain.Message,
            },
            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = "The request is invalid.",
                Detail = badRequest.Message,
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Something went wrong on our side.",
                Detail = "An unexpected error occurred. Try again; if it keeps happening, report the trace id.",
            },
        };

        problem.Extensions["code"] = exception switch
        {
            AppException app => app.Code,
            DomainException => "invalid_state",
            BadHttpRequestException => "bad_request",
            _ => "internal_error",
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (exception is GitHubRateLimitException { ResetAt: { } resetAt })
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((resetAt - DateTimeOffset.UtcNow).TotalSeconds));
            httpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var code = problem.Extensions["code"]?.ToString() ?? string.Empty;
        if (exception is not (AppException or DomainException or BadHttpRequestException))
        {
            LogUnhandled(exception, httpContext.Request.Method, httpContext.Request.Path);
        }
        else if (problem.Status >= 500)
        {
            LogDependencyUnavailable(problem.Status ?? 0, code, httpContext.Request.Path, exception.Message);
        }
        else
        {
            LogHandled(problem.Status ?? 0, code, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => "The request is invalid.",
        ErrorKind.Unauthorized => "Sign-in required.",
        ErrorKind.Forbidden => "Not allowed.",
        ErrorKind.NotFound => "Not found.",
        ErrorKind.Conflict => "The request conflicts with the current state.",
        ErrorKind.RateLimited => "Too many requests.",
        ErrorKind.Unavailable => "A service RepoLens depends on is unavailable.",
        _ => "Something went wrong on our side.",
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private partial void LogUnhandled(Exception exception, string method, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request failed with {Status} ({Code}) for {Path}")]
    private partial void LogHandled(int status, string code, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request failed with {Status} ({Code}) for {Path}: {Reason}")]
    private partial void LogDependencyUnavailable(int status, string code, string path, string reason);
}
