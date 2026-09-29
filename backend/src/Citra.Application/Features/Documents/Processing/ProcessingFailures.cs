using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Extraction;
using Citra.Application.Common.Persistence;
using Citra.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Citra.Application.Features.Documents.Processing;

/// <summary>
/// Records what happens after a processing attempt failed: the job is retried later (30 s, then 2 min) until its
/// attempts run out, then the job and its document are marked Failed, with a message the user can act on.
/// Errors that another attempt cannot fix (a scanned PDF stays scanned) fail at once.
/// Always works in a fresh unit of work: the failed attempt's one may still hold unsaved chunks.
/// </summary>
internal sealed class ProcessingFailures(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ProcessingFailures> logger)
{
    internal const string UnexpectedErrorMessage =
        "Something went wrong while processing this document. Try Reprocess; if it keeps failing, upload the file again.";

    internal const string KeptStoppingMessage =
        "Processing stopped unexpectedly several times. Try Reprocess; if it keeps failing, the file may be too large or damaged.";

    /// <summary>A processing attempt threw <paramref name="error"/>: retry the job later, or give up.</summary>
    public Task RecordAsync(ProcessingJob job, Exception error, CancellationToken cancellationToken) =>
        SaveAsync(job, error, UserMessage(error), cancellationToken);

    /// <summary>
    /// The job was taken over after its lock ran out more often than it may be tried: earlier attempts never
    /// finished at all (e.g. the worker crashed on this document every time), so it is not run again.
    /// </summary>
    public Task GiveUpAsync(ProcessingJob job, CancellationToken cancellationToken) =>
        SaveAsync(job, error: null, KeptStoppingMessage, cancellationToken);

    private async Task SaveAsync(ProcessingJob claimed, Exception? error, string userMessage, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Only a job that is still on this worker's attempt is ours to change.
        var job = await db.ProcessingJobs.SingleOrDefaultAsync(
            j => j.Id == claimed.Id && j.Attempts == claimed.Attempts && j.Status == ProcessingJobStatus.Running,
            cancellationToken);
        if (job is null)
        {
            logger.LogWarning(
                error, "Processing job {JobId} failed, but it was taken over by another worker or deleted with its document meanwhile",
                claimed.Id);
            return;
        }

        var document = await db.Documents
            .IgnoreQueryFilters([QueryFilters.Tenant]) // the Worker has no logged-in user
            .SingleOrDefaultAsync(d => d.Id == job.DocumentId, cancellationToken);
        if (document is null)
        {
            logger.LogWarning(error, "Processing job {JobId} failed, but its document was deleted meanwhile", claimed.Id);
            return;
        }

        var now = timeProvider.GetUtcNow();
        var details = error is null ? KeptStoppingMessage : Describe(error);

        bool gaveUp;
        if (error is null || IsPermanent(error, document))
        {
            job.GiveUp(details, now);
            gaveUp = true;
        }
        else
        {
            gaveUp = job.Fail(details, now);
        }

        if (gaveUp)
        {
            document.MarkFailed(userMessage, now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Taken over or deleted between loading and saving: not ours any more.
            logger.LogWarning(error, "Processing job {JobId} failed, but it was taken over or deleted while recording it", job.Id);
            return;
        }

        if (gaveUp)
        {
            logger.LogError(
                error, "Gave up processing document {DocumentId} after attempt {Attempt}: {UserMessage}",
                document.Id, job.Attempts, userMessage);
        }
        else
        {
            logger.LogWarning(
                error, "Processing document {DocumentId} failed (attempt {Attempt} of {MaxAttempts}); retrying at {NextAttemptAt:O}",
                document.Id, job.Attempts, ProcessingJob.MaxAttempts, job.NextAttemptAt);
        }
    }

    /// <summary>
    /// A file that cannot be read now cannot be read on the next attempt either (scanned, empty, damaged, missing).
    /// A web page can: the site may just be down for a moment. Everything else may be temporary and is retried.
    /// </summary>
    private static bool IsPermanent(Exception error, Document document) =>
        error is TextExtractionException && document.SourceType != DocumentSourceType.Url;

    /// <summary>What the documents page shows. Only messages written for users are passed on as they are.</summary>
    internal static string UserMessage(Exception error) => error switch
    {
        TextExtractionException extraction => extraction.Message,
        EmbeddingException embedding => $"The text could not be prepared for search: {embedding.Message} Try Reprocess later.",
        _ => UnexpectedErrorMessage,
    };

    /// <summary>For the job's LastError (operators): every exception in the chain, type and message.</summary>
    private static string Describe(Exception error)
    {
        var parts = new List<string>();
        for (var current = error; current is not null; current = current.InnerException)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join(" → ", parts);
    }
}
