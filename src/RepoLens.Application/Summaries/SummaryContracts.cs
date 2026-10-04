using RepoLens.Application.Common.Abstractions;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries;

public sealed record SummaryJobDto(
    Guid Id,
    Guid RepoId,
    string RepoFullName,
    string Branch,
    string? CommitSha,
    SummaryStatus Status,
    SummaryStage Stage,
    int Progress,
    int Attempts,
    JobErrorDto? Error,
    PdfDto? Pdf,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string StatusUrl)
{
    public static SummaryJobDto From(SummaryJob job, IResourceLinks links) => new(
        job.Id,
        job.RepoId,
        job.RepoFullName,
        job.Branch,
        job.CommitSha,
        job.Status,
        job.Stage,
        job.Progress,
        job.Attempts,
        job.Error is null ? null : new JobErrorDto(job.Error.Code, job.Error.Message),
        job.Pdf is null ? null : new PdfDto(job.Pdf.FileName, job.Pdf.SizeBytes, links.SummaryPdf(job.Id)),
        job.CreatedAt,
        job.StartedAt,
        job.CompletedAt,
        links.SummaryStatus(job.Id));
}

public sealed record JobErrorDto(string Code, string Message);

public sealed record PdfDto(string FileName, long SizeBytes, string Url);

public enum SummaryRequestOutcome
{
    /// <summary>A new job was queued.</summary>
    Created,

    /// <summary>A job for this repository was already queued or running; it is returned instead.</summary>
    AlreadyInProgress,

    /// <summary>This exact commit was already summarized; the existing PDF is returned.</summary>
    ReusedCompleted,
}

public sealed record SummaryRequestResult(SummaryJobDto Job, SummaryRequestOutcome Outcome);

/// <summary>An open PDF stream plus what the API needs to send it.</summary>
public sealed record SummaryPdfDownload(Stream Content, string FileName, string ContentType, long SizeBytes, string ETag);
