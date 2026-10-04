using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries.Processing.Steps;

/// <summary>Pins the job to a commit if the API could not resolve one when the job was requested.</summary>
public sealed class ResolveCommitStep(IGitHubClient gitHub) : ISummaryPipelineStep
{
    public int Order => 100;

    public SummaryStage Stage => SummaryStage.ResolvingCommit;

    public int StartProgress => 3;

    public async Task ExecuteAsync(SummaryPipelineContext context, CancellationToken cancellationToken)
    {
        if (context.Job.CommitSha is not null)
        {
            return;
        }

        var repo = context.Repo;
        var sha = await gitHub.GetBranchHeadShaAsync(context.AccessToken, repo.Owner, repo.Name, context.Job.Branch, cancellationToken);
        context.Job.PinCommit(sha);
    }
}
