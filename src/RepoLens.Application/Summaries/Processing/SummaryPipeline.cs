using RepoLens.Application.Common.Abstractions.Persistence;

namespace RepoLens.Application.Summaries.Processing;

/// <summary>Runs the pipeline steps in order and records progress on the job as it goes.</summary>
public sealed class SummaryPipeline(
    IEnumerable<ISummaryPipelineStep> steps,
    ISummaryJobRepository jobs,
    TimeProvider timeProvider)
{
    private const int FinalProgress = 99;

    private readonly IReadOnlyList<ISummaryPipelineStep> _steps = steps.OrderBy(s => s.Order).ToList();

    public async Task RunAsync(SummaryPipelineContext context, CancellationToken cancellationToken)
    {
        // Steps may report progress from parallel tasks (the map phase); saves must not interleave.
        using var saveLock = new SemaphoreSlim(1, 1);
        var lastSavedProgress = -1;
        var lastSavedStep = -1;

        for (var i = 0; i < _steps.Count; i++)
        {
            var stepIndex = i;
            var step = _steps[i];
            var start = step.StartProgress;
            var end = i + 1 < _steps.Count ? _steps[i + 1].StartProgress : FinalProgress;

            async Task SaveAsync(int progress, CancellationToken ct)
            {
                await saveLock.WaitAsync(ct);
                try
                {
                    if (stepIndex == lastSavedStep && progress <= lastSavedProgress)
                    {
                        return;
                    }

                    context.Job.ReportProgress(step.Stage, progress, timeProvider.GetUtcNow());
                    await jobs.SaveProgressAsync(context.Job, ct);
                    lastSavedProgress = progress;
                    lastSavedStep = stepIndex;
                }
                finally
                {
                    saveLock.Release();
                }
            }

            context.ReportProgress = (fraction, ct) =>
                SaveAsync(start + (int)((end - start) * Math.Clamp(fraction, 0, 1)), ct);

            await SaveAsync(start, cancellationToken);
            await step.ExecuteAsync(context, cancellationToken);
        }
    }
}
