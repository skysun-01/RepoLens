using RepoLens.Application.Common.Abstractions.Storage;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries.Processing.Steps;

public sealed class StorePdfStep(IFileStorage fileStorage) : ISummaryPipelineStep
{
    public int Order => 600;

    public SummaryStage Stage => SummaryStage.StoringPdf;

    public int StartProgress => 95;

    public async Task ExecuteAsync(SummaryPipelineContext context, CancellationToken cancellationToken)
    {
        var job = context.Job;
        var repo = context.Repo;
        var shortSha = context.CommitSha[..Math.Min(7, context.CommitSha.Length)];
        var key = $"summaries/{job.UserId:N}/{job.RepoId:N}/{job.Id:N}.pdf";
        var fileName = $"{repo.Name}-summary-{shortSha}.pdf";

        using var content = new MemoryStream(context.RequirePdf(), writable: false);
        context.StoredPdf = await fileStorage.SaveAsync(key, fileName, "application/pdf", content, cancellationToken);
    }
}
