namespace RepoLens.Application.Common.Errors;

/// <summary>What kind of failure an <see cref="AppException"/> is; the API maps each kind to an HTTP status.</summary>
public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    RateLimited,
    Unavailable,
}

/// <summary>
/// An expected failure with a stable <see cref="Code"/> clients can switch on
/// (for example <c>repo_not_indexed</c>) and a message a person can act on.
/// </summary>
public abstract class AppException(string code, string message, ErrorKind kind, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;

    public ErrorKind Kind { get; } = kind;
}

public sealed class NotFoundException(string resource, object id)
    : AppException("not_found", $"{resource} '{id}' was not found.", ErrorKind.NotFound);

public sealed class ValidationFailedException(string message, IReadOnlyDictionary<string, string[]>? errors = null)
    : AppException("validation_failed", message, ErrorKind.Validation)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}

public sealed class ConflictException(string code, string message)
    : AppException(code, message, ErrorKind.Conflict);

public sealed class UnauthorizedException(string code, string message)
    : AppException(code, message, ErrorKind.Unauthorized);

/// <summary>Another writer saved the same document first. The caller can reload and retry.</summary>
public sealed class ConcurrencyException(string entity, object id)
    : AppException("concurrent_update", $"{entity} '{id}' was changed by another request. Try again.", ErrorKind.Conflict);
