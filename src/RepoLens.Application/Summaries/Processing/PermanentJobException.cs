using RepoLens.Application.Common.Errors;

namespace RepoLens.Application.Summaries.Processing;

/// <summary>A failure that retrying cannot fix. The job is marked failed immediately instead of being redelivered.</summary>
public sealed class PermanentJobException(string code, string message)
    : AppException(code, message, ErrorKind.Validation);
