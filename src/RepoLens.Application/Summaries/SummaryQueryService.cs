using RepoLens.Application.Common.Abstractions;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Abstractions.Storage;
using RepoLens.Application.Common.Errors;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries;

public sealed class SummaryQueryService(
    IRepoRepository repos,
    ISummaryJobRepository jobs,
    IFileStorage fileStorage,
    IResourceLinks links)
{
    private const int HistoryLimit = 50;

    public async Task<SummaryJobDto> GetAsync(Guid userId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetForUserAsync(userId, jobId, cancellationToken) ?? throw new NotFoundException("Summary", jobId);
        return SummaryJobDto.From(job, links);
    }

    public async Task<IReadOnlyList<SummaryJobDto>> ListForRepoAsync(Guid userId, Guid repoId, CancellationToken cancellationToken)
    {
        _ = await repos.GetAsync(userId, repoId, cancellationToken) ?? throw new NotFoundException("Repository", repoId);
        var history = await jobs.ListForRepoAsync(userId, repoId, HistoryLimit, cancellationToken);
        return history.Select(job => SummaryJobDto.From(job, links)).ToList();
    }

    public async Task<SummaryPdfDownload> OpenPdfAsync(Guid userId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetForUserAsync(userId, jobId, cancellationToken) ?? throw new NotFoundException("Summary", jobId);

        if (job.Status != SummaryStatus.Completed || job.Pdf is null)
        {
            throw new ConflictException("summary_not_ready", $"This summary is {job.Status.ToString().ToLowerInvariant()}; the PDF is available once it completes.");
        }

        var stream = await fileStorage.OpenReadAsync(job.Pdf, cancellationToken)
            ?? throw new NotFoundException("Summary PDF", jobId);

        return new SummaryPdfDownload(stream, job.Pdf.FileName, job.Pdf.ContentType, job.Pdf.SizeBytes, $"\"{job.Id:N}\"");
    }
}
