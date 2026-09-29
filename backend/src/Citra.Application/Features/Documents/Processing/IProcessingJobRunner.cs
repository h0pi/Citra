namespace Citra.Application.Features.Documents.Processing;

/// <summary>
/// Processes documents in the background: the Worker's polling loop calls <see cref="RunNextAsync"/>
/// in a fresh DI scope (its own database unit of work) for every job.
/// </summary>
public interface IProcessingJobRunner
{
    /// <summary>
    /// Runs the next due processing job, if there is one. Returns true when a job was run, so the caller
    /// checks again right away instead of waiting for the next poll; false when there was nothing to do.
    /// </summary>
    Task<bool> RunNextAsync(CancellationToken cancellationToken);
}
