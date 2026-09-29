using Citra.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Citra.Application.Features.Documents.Processing;

internal sealed class ProcessingJobRunner(
    IProcessingJobQueue queue,
    DocumentPipeline pipeline,
    ProcessingFailures failures,
    ILogger<ProcessingJobRunner> logger) : IProcessingJobRunner
{
    public async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        var job = await queue.ClaimNextAsync(cancellationToken);
        if (job is null)
        {
            return false;
        }

        logger.LogInformation(
            "Took processing job {JobId} for document {DocumentId} (attempt {Attempt})",
            job.Id, job.DocumentId, job.Attempts);

        // More attempts than allowed only happens when earlier ones never ended (a failure is always recorded):
        // the worker crashed or was killed on this document each time. Don't try it yet again.
        if (job.Attempts > ProcessingJob.MaxAttempts)
        {
            await failures.GiveUpAsync(job, cancellationToken);
            return true;
        }

        // While the pipeline runs, its lock is renewed. If the job stops being ours (taken over after the lock
        // ran out), processing is cancelled, so two workers never both finish the same document.
        using var stopHolding = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var processing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var holding = HoldAsync(job, stopHolding.Token, processing);
        Exception? failure = null;

        try
        {
            // On success the pipeline itself marks the job Succeeded, in the same transaction as the chunks.
            await pipeline.RunAsync(job, processing.Token);
        }
        catch (OperationCanceledException) when (processing.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            LogTakenOver(job);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The final save found the job on another attempt (or deleted with its document): nothing was saved.
            LogTakenOver(job);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A shutdown is not a failure: the job keeps its lock and is taken up again after a restart.
            failure = ex;
        }
        finally
        {
            await stopHolding.CancelAsync();
            await holding;
        }

        if (failure is not null)
        {
            // Not cancellable: a real failure is still recorded if the Worker starts stopping meanwhile (one short
            // save), or the job would sit locked until its lock runs out and the error would be lost.
            await failures.RecordAsync(job, failure, CancellationToken.None);
        }

        return true;
    }

    private async Task HoldAsync(ProcessingJob job, CancellationToken stop, CancellationTokenSource processing)
    {
        if (!await queue.HoldAsync(job, stop))
        {
            await processing.CancelAsync();
        }
    }

    private void LogTakenOver(ProcessingJob job) =>
        logger.LogWarning(
            "Stopped processing document {DocumentId}: job {JobId} was taken over by another worker or deleted",
            job.DocumentId, job.Id);
}
