using RepoLens.Domain.Common;

namespace RepoLens.Domain.Summaries;

/// <summary>
/// One request to summarize a repository. Moves through
/// Queued → Processing → Completed | Failed, and back to Queued when a transient error is retried.
/// </summary>
public sealed class SummaryJob : Entity
{
    private SummaryJob()
    {
    }

    private SummaryJob(Guid id, Guid userId, Guid repoId, string repoFullName, string branch, string? commitSha, bool force, DateTimeOffset now)
        : base(id, now)
    {
        UserId = userId;
        RepoId = repoId;
        RepoFullName = repoFullName;
        Branch = branch;
        CommitSha = commitSha;
        Force = force;
        Status = SummaryStatus.Queued;
        Stage = SummaryStage.Queued;
        IsActive = true;
    }

    public Guid UserId { get; private set; }

    public Guid RepoId { get; private set; }

    public string RepoFullName { get; private set; } = string.Empty;

    public string Branch { get; private set; } = string.Empty;

    /// <summary>The commit being summarized. Pinned at request time when GitHub answers, otherwise by the worker.</summary>
    public string? CommitSha { get; private set; }

    /// <summary>True when the user asked to regenerate even though this commit was already summarized.</summary>
    public bool Force { get; private set; }

    public SummaryStatus Status { get; private set; }

    public SummaryStage Stage { get; private set; }

    /// <summary>0–100, for a progress bar.</summary>
    public int Progress { get; private set; }

    /// <summary>How many times a worker has started this job.</summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// True while Queued or Processing. Stored so a unique partial index can allow
    /// at most one active job per repository.
    /// </summary>
    public bool IsActive { get; private set; }

    public JobError? Error { get; private set; }

    public StoredFile? Pdf { get; private set; }

    public RepoSummary? Summary { get; private set; }

    /// <summary>Outbox marker: when the GenerateSummary command reached the message broker.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? HeartbeatAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static SummaryJob Create(Guid userId, Guid repoId, string repoFullName, string branch, string? commitSha, bool force, DateTimeOffset now)
    {
        if (userId == Guid.Empty || repoId == Guid.Empty)
        {
            throw new DomainException("A summary job needs a user and a repository.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(repoFullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        return new SummaryJob(Guid.CreateVersion7(now), userId, repoId, repoFullName, branch, commitSha, force, now);
    }

    /// <summary>
    /// Whether a worker may take this job now: it is queued, or a previous worker stopped
    /// sending heartbeats (crashed) while processing it.
    /// </summary>
    public bool CanBeClaimed(DateTimeOffset now, TimeSpan staleAfter) =>
        Status == SummaryStatus.Queued
        || (Status == SummaryStatus.Processing && IsHeartbeatStale(now, staleAfter));

    public bool IsHeartbeatStale(DateTimeOffset now, TimeSpan staleAfter) =>
        Status == SummaryStatus.Processing && (HeartbeatAt ?? StartedAt ?? CreatedAt) < now - staleAfter;

    public void MarkPublished(DateTimeOffset now)
    {
        PublishedAt ??= now;
        Touch(now);
    }

    public void Start(DateTimeOffset now, TimeSpan staleAfter)
    {
        if (!CanBeClaimed(now, staleAfter))
        {
            throw new DomainException($"Job {Id} cannot be started while it is {Status}.");
        }

        Status = SummaryStatus.Processing;
        Stage = SummaryStage.Starting;
        Progress = Math.Max(Progress, 1);
        Attempts++;
        StartedAt ??= now;
        HeartbeatAt = now;
        PublishedAt ??= now;
        Error = null;
        Touch(now);
    }

    public void PinCommit(string commitSha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);
        EnsureStatus(SummaryStatus.Processing);

        if (CommitSha is not null && !string.Equals(CommitSha, commitSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException($"Job {Id} is already pinned to commit {CommitSha}.");
        }

        CommitSha = commitSha;
    }

    public void ReportProgress(SummaryStage stage, int progress, DateTimeOffset now)
    {
        EnsureStatus(SummaryStatus.Processing);

        if (stage is SummaryStage.Completed or SummaryStage.Failed or SummaryStage.Queued)
        {
            throw new DomainException("Use Complete, Fail or ReturnToQueue for terminal transitions.");
        }

        Stage = stage;
        Progress = Math.Clamp(progress, Progress, 99);
        HeartbeatAt = now;
        Touch(now);
    }

    public void Complete(StoredFile pdf, RepoSummary summary, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(summary);
        EnsureStatus(SummaryStatus.Processing);

        if (CommitSha is null)
        {
            throw new DomainException("A job cannot complete before its commit is pinned.");
        }

        Status = SummaryStatus.Completed;
        Stage = SummaryStage.Completed;
        Progress = 100;
        IsActive = false;
        Pdf = pdf;
        Summary = summary;
        Error = null;
        CompletedAt = now;
        HeartbeatAt = now;
        Touch(now);
    }

    public void Fail(JobError error, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (Status is SummaryStatus.Completed or SummaryStatus.Failed)
        {
            throw new DomainException($"Job {Id} already finished as {Status}.");
        }

        Status = SummaryStatus.Failed;
        Stage = SummaryStage.Failed;
        IsActive = false;
        Error = error;
        CompletedAt = now;
        Touch(now);
    }

    /// <summary>
    /// A transient failure: the message broker will redeliver the command, so the job waits in the queue
    /// again. The error is kept so the UI can show why it is retrying.
    /// </summary>
    public void ReturnToQueue(JobError lastError, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lastError);
        EnsureStatus(SummaryStatus.Processing);

        Status = SummaryStatus.Queued;
        Stage = SummaryStage.Queued;
        Error = lastError;
        Touch(now);
    }

    /// <summary>
    /// The worker processing this job stopped sending heartbeats. Queue it again and clear the
    /// outbox marker so the dispatcher publishes a fresh command.
    /// </summary>
    public void RequeueStale(DateTimeOffset now, TimeSpan staleAfter)
    {
        if (!IsHeartbeatStale(now, staleAfter))
        {
            throw new DomainException($"Job {Id} is not stale.");
        }

        Status = SummaryStatus.Queued;
        Stage = SummaryStage.Queued;
        PublishedAt = null;
        Error = new JobError("worker_timeout", "The worker stopped responding. The job was queued again.");
        Touch(now);
    }

    private void EnsureStatus(SummaryStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainException($"Job {Id} is {Status}, expected {expected}.");
        }
    }
}
