using Microsoft.Extensions.Logging;
using RepoLens.Application.Common.Abstractions;
using RepoLens.Application.Common.Abstractions.GitHub;
using RepoLens.Application.Common.Abstractions.Messaging;
using RepoLens.Application.Common.Abstractions.Persistence;
using RepoLens.Application.Common.Errors;
using RepoLens.Application.Users;
using RepoLens.Domain.Summaries;

namespace RepoLens.Application.Summaries;

/// <summary>
/// Handles the Summarize button. Responds in milliseconds: it stores a job and publishes a command;
/// the slow work happens in the worker.
/// </summary>
public sealed partial class SummaryRequestService(
    IRepoRepository repos,
    ISummaryJobRepository jobs,
    GitHubTokenAccessor tokenAccessor,
    IGitHubClient gitHub,
    IMessagePublisher publisher,
    IResourceLinks links,
    TimeProvider timeProvider,
    ILogger<SummaryRequestService> logger)
{
    public async Task<SummaryRequestResult> RequestAsync(Guid userId, Guid repoId, bool force, CancellationToken cancellationToken)
    {
        var repo = await repos.GetAsync(userId, repoId, cancellationToken) ?? throw new NotFoundException("Repository", repoId);

        var active = await jobs.GetActiveForRepoAsync(repo.Id, cancellationToken);
        if (active is not null)
        {
            return Result(active, SummaryRequestOutcome.AlreadyInProgress);
        }

        var commitSha = await TryResolveHeadAsync(userId, repo.Owner, repo.Name, repo.DefaultBranch, cancellationToken);

        if (!force && commitSha is not null)
        {
            var completed = await jobs.GetLatestCompletedAsync(repo.Id, commitSha, cancellationToken);
            if (completed is not null)
            {
                return Result(completed, SummaryRequestOutcome.ReusedCompleted);
            }
        }

        var job = SummaryJob.Create(userId, repo.Id, repo.FullName, repo.DefaultBranch, commitSha, force, timeProvider.GetUtcNow());

        if (!await jobs.TryAddAsync(job, cancellationToken))
        {
            // Another request created a job between our check and insert.
            var winner = await jobs.GetActiveForRepoAsync(repo.Id, cancellationToken)
                ?? throw new ConflictException("summary_conflict", "Another summary request for this repository just finished. Try again.");
            return Result(winner, SummaryRequestOutcome.AlreadyInProgress);
        }

        await TryPublishAsync(job, cancellationToken);
        LogQueued(job.Id, repo.FullName, commitSha ?? "(unresolved)");
        return Result(job, SummaryRequestOutcome.Created);
    }

    /// <summary>
    /// Pins the commit up front so an unchanged repository can reuse its PDF instantly. If GitHub is
    /// briefly unavailable the worker resolves the commit later. Authorization and missing-repository
    /// errors are the user's to fix, so they propagate.
    /// </summary>
    private async Task<string?> TryResolveHeadAsync(Guid userId, string owner, string name, string branch, CancellationToken cancellationToken)
    {
        var (_, accessToken) = await tokenAccessor.GetAsync(userId, cancellationToken);
        try
        {
            return await gitHub.GetBranchHeadShaAsync(accessToken, owner, name, branch, cancellationToken);
        }
        catch (Exception ex) when (ex is GitHubRateLimitException or GitHubUnavailableException)
        {
            LogHeadUnresolved(ex, owner, name);
            return null;
        }
    }

    /// <summary>
    /// Publishes right away. On failure the job keeps its empty outbox marker and the outbox
    /// dispatcher publishes it on its next pass, so the request is never lost.
    /// </summary>
    private async Task TryPublishAsync(SummaryJob job, CancellationToken cancellationToken)
    {
        try
        {
            await publisher.PublishAsync(GenerateSummaryCommand.For(job), cancellationToken);
            var now = timeProvider.GetUtcNow();
            job.MarkPublished(now);
            await jobs.MarkPublishedAsync(job.Id, now, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPublishDeferred(ex, job.Id);
        }
    }

    private SummaryRequestResult Result(SummaryJob job, SummaryRequestOutcome outcome) =>
        new(SummaryJobDto.From(job, links), outcome);

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued summary job {JobId} for {Repo} at {CommitSha}")]
    private partial void LogQueued(Guid jobId, string repo, string commitSha);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not resolve the head commit of {Owner}/{Name}; the worker will retry")]
    private partial void LogHeadUnresolved(Exception exception, string owner, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing job {JobId} failed; the outbox dispatcher will publish it")]
    private partial void LogPublishDeferred(Exception exception, Guid jobId);
}
