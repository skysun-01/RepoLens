namespace RepoLens.Domain.Summaries;

/// <summary>Why a job failed: a stable machine-readable code plus a message a user can act on.</summary>
public sealed record JobError(string Code, string Message);
