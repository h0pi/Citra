using Citra.Domain.Documents;

namespace Citra.Application.Features.Documents.Processing;

/// <summary>The processing_jobs table seen as a work queue shared by any number of Workers.</summary>
public interface IProcessingJobQueue
{
    /// <summary>
    /// Takes the oldest due job (see <see cref="ProcessingJob.CanStart"/>) for this worker: marks it Running,
    /// counts the attempt and locks it for the configured lock duration. Safe with several workers at once:
    /// every job is taken by exactly one of them, and nobody waits for another worker's claim.
    /// Returns null when no job is due. The returned job is tracked by the current unit of work.
    /// </summary>
    Task<ProcessingJob?> ClaimNextAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Keeps a taken job's lock alive while it is processed (a big document on a slow embedding plan can take
    /// longer than the lock), renewing it every third of the lock duration. Returns true when
    /// <paramref name="stop"/> is cancelled (processing ended normally), false as soon as the job is no longer
    /// this worker's: another worker took it over after the lock ran out, or it was deleted with its document.
    /// Uses its own database connection, never the caller's unit of work, so it can run alongside processing.
    /// </summary>
    Task<bool> HoldAsync(ProcessingJob job, CancellationToken stop);
}
