using Citra.Domain.Common;

namespace Citra.Domain.Documents;

/// <summary>
/// A unit of background work for the Worker: "process this document".
/// A job is locked while running, so two workers never process the same document; if a worker crashes,
/// the lock expires and another worker can take the job over.
/// </summary>
public sealed class ProcessingJob : Entity
{
    public const int MaxAttempts = 3;
    public const int LastErrorMaxLength = 4000;

    public Guid DocumentId { get; private set; }

    public ProcessingJobStatus Status { get; private set; } = ProcessingJobStatus.Pending;

    public int Attempts { get; private set; }

    /// <summary>While Running: the job belongs to the current worker until this time.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    /// <summary>Earliest time a Pending job may be picked up (used to delay retries).</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    private ProcessingJob()
    {
        // For EF Core.
    }

    public static ProcessingJob For(Guid documentId)
    {
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("Document id is required.", nameof(documentId));
        }

        return new ProcessingJob { DocumentId = documentId };
    }

    /// <summary>True if a worker may pick this job up now (pending and due, or running with an expired lock).</summary>
    public bool CanStart(DateTimeOffset now) =>
        Status switch
        {
            ProcessingJobStatus.Pending => NextAttemptAt is null || NextAttemptAt <= now,
            ProcessingJobStatus.Running => LockedUntil is null || LockedUntil <= now,
            _ => false,
        };

    public void Start(DateTimeOffset now, TimeSpan lockDuration)
    {
        if (!CanStart(now))
        {
            throw new InvalidOperationException($"Job {Id} cannot start (status {Status}).");
        }

        Status = ProcessingJobStatus.Running;
        Attempts++;
        LockedUntil = now + lockDuration;
        NextAttemptAt = null;
    }

    public void Succeed(DateTimeOffset now)
    {
        EnsureRunning();
        Status = ProcessingJobStatus.Succeeded;
        LockedUntil = null;
        LastError = null;
        CompletedAt = now;
    }

    /// <summary>
    /// Records a failed attempt. Retries later (with growing delay: 30s, 2min, ...) until
    /// <see cref="MaxAttempts"/> is reached. Returns true if the job gave up for good.
    /// </summary>
    public bool Fail(string error, DateTimeOffset now)
    {
        EnsureRunning();
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        LastError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
        LockedUntil = null;

        if (Attempts >= MaxAttempts)
        {
            Status = ProcessingJobStatus.Failed;
            CompletedAt = now;
            return true;
        }

        Status = ProcessingJobStatus.Pending;
        NextAttemptAt = now + TimeSpan.FromSeconds(30 * Math.Pow(4, Attempts - 1));
        return false;
    }

    private void EnsureRunning()
    {
        if (Status != ProcessingJobStatus.Running)
        {
            throw new InvalidOperationException($"Job {Id} is not running (status {Status}).");
        }
    }
}
