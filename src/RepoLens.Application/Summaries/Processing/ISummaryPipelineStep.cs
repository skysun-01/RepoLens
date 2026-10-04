using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries.Processing;

/// <summary>
/// One stage of turning a repository into a PDF. The pipeline runs all registered steps in
/// <see cref="Order"/>, so adding a stage means adding a class, not editing the pipeline.
/// </summary>
public interface ISummaryPipelineStep
{
    int Order { get; }

    SummaryStage Stage { get; }

    /// <summary>Progress shown when the step starts; the step's own progress fills up to the next step's value.</summary>
    int StartProgress { get; }

    Task ExecuteAsync(SummaryPipelineContext context, CancellationToken cancellationToken);
}
