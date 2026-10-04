using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Common.Abstractions.Documents;

/// <summary>Lays out a <see cref="RepoSummary"/> as a PDF.</summary>
public interface ISummaryPdfRenderer
{
    byte[] Render(RepoSummary summary, SummaryDocumentInfo info);
}

/// <summary>Facts about the summarized repository shown on the cover and in page headers.</summary>
public sealed record SummaryDocumentInfo(
    string RepoFullName,
    string? Description,
    string HtmlUrl,
    string Branch,
    string CommitSha,
    string? PrimaryLanguage,
    int FilesAnalyzed,
    DateTimeOffset GeneratedAt);
