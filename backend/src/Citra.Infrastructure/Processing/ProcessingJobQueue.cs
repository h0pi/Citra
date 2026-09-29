using Citra.Application.Features.Documents.Processing;
using Citra.Domain.Documents;
using Citra.Infrastructure.Options;
using Citra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Citra.Infrastructure.Processing;

/// <summary>
/// Claims jobs with PostgreSQL row locks. In one short transaction: pick the oldest due row with
/// FOR UPDATE SKIP LOCKED, mark it Running (with a time-limited lock) and commit. A row another worker is
/// claiming at that moment is skipped instead of waited for, so no two workers ever take the same job and
/// nobody blocks. After the commit the row lock is gone, but the job is Running with a lock time in the future,
/// so it is no longer due for anyone else.
/// </summary>
internal sealed class ProcessingJobQueue(
    CitraDbContext db,
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider,
    IOptions<ProcessingOptions> options,
    ILogger<ProcessingJobQueue> logger) : IProcessingJobQueue
{
    private static readonly string Pending = nameof(ProcessingJobStatus.Pending);
    private static readonly string Running = nameof(ProcessingJobStatus.Running);

    public async Task<ProcessingJob?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Same rule as ProcessingJob.CanStart: pending and due, or running with an expired lock (worker crashed).
        // ToListAsync, not SingleOrDefaultAsync: EF must not wrap this SQL in an outer query.
        var candidates = await db.ProcessingJobs
            .FromSql($"""
                SELECT * FROM processing_jobs
                WHERE (status = {Pending} AND (next_attempt_at IS NULL OR next_attempt_at <= {now}))
                   OR (status = {Running} AND (locked_until IS NULL OR locked_until <= {now}))
                ORDER BY created_at, id
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var job = candidates.SingleOrDefault();
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        job.Start(now, options.Value.LockDuration);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    public async Task<bool> HoldAsync(ProcessingJob job, CancellationToken stop)
    {
        var lockDuration = options.Value.LockDuration;
        var renewEvery = lockDuration / 3;

        // The attempt number proves ownership: a worker taking the job over counts a new attempt.
        var attempt = job.Attempts;

        try
        {
            while (true)
            {
                await Task.Delay(renewEvery, timeProvider, stop);

                bool renewed;
                try
                {
                    renewed = await RenewAsync(job.Id, attempt, lockDuration, stop);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // E.g. the database is briefly unreachable: the lock is still valid for a while, try again later.
                    logger.LogWarning(ex, "Could not renew the lock of processing job {JobId}; trying again in {RenewEvery}", job.Id, renewEvery);
                    continue;
                }

                if (!renewed)
                {
                    logger.LogWarning(
                        "Processing job {JobId} (attempt {Attempt}) is no longer this worker's: taken over by another worker or deleted",
                        job.Id, attempt);
                    return false;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return true;
        }
    }

    /// <summary>Moves the lock forward, only while the job is still Running with this worker's attempt.</summary>
    private async Task<bool> RenewAsync(Guid jobId, int attempt, TimeSpan lockDuration, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var command = dataSource.CreateCommand(
            "UPDATE processing_jobs SET locked_until = $1, updated_at = $2 WHERE id = $3 AND status = $4 AND attempts = $5");
        command.Parameters.Add(new NpgsqlParameter { Value = now + lockDuration });
        command.Parameters.Add(new NpgsqlParameter { Value = now });
        command.Parameters.Add(new NpgsqlParameter { Value = jobId });
        command.Parameters.Add(new NpgsqlParameter { Value = Running });
        command.Parameters.Add(new NpgsqlParameter { Value = attempt });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
