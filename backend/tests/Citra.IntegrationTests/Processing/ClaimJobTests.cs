using System.Collections.Concurrent;
using Citra.Application.Features.Documents.Processing;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Infrastructure.Options;
using Citra.Infrastructure.Processing;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Processing;

/// <summary>
/// C-141: which jobs the Worker takes and that taking them is safe with several workers at once
/// (FOR UPDATE SKIP LOCKED), checked in PostgreSQL. Runs on its own database: every test starts with an empty
/// job table, because the queue always takes "the oldest due job" of all.
/// </summary>
[Collection(IsolatedDatabaseCollection.Name)]
public sealed class ClaimJobTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Lock = new ProcessingOptions().LockDuration;

    public async ValueTask InitializeAsync()
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Db.ProcessingJobs.ExecuteDeleteAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Only_jobs_a_worker_may_start_now_are_taken()
    {
        var now = DateTimeOffset.UtcNow;
        var document = await SeedDocumentAsync();
        var jobs = new Dictionary<string, ProcessingJob>
        {
            ["new"] = ProcessingJob.For(document.Id),
            ["retry, due"] = Retried(document.Id, failedAt: now.AddHours(-1)),
            ["retry, waiting"] = Retried(document.Id, failedAt: now.AddHours(1)),
            ["running, locked"] = Running(document.Id, startedAt: now),
            ["running, lock expired (worker crashed)"] = Running(document.Id, startedAt: now - Lock - TimeSpan.FromMinutes(1)),
            ["succeeded"] = Succeeded(document.Id, now),
            ["failed for good"] = FailedForGood(document.Id, now),
        };
        await SaveAsync(jobs.Values);

        var taken = await ClaimAllAsync();

        jobs.Where(j => taken.Contains(j.Value.Id)).Select(j => j.Key).ShouldBe(
            ["new", "retry, due", "running, lock expired (worker crashed)"], ignoreOrder: true);
        foreach (var (name, job) in jobs)
        {
            taken.Contains(job.Id).ShouldBe(job.CanStart(now), $"database and domain agree: {name}");
        }
    }

    [Fact]
    public async Task A_taken_job_is_running_locked_and_no_longer_due()
    {
        var document = await SeedDocumentAsync();
        var job = ProcessingJob.For(document.Id);
        await SaveAsync([job]);
        var before = DateTimeOffset.UtcNow;

        var claimed = await ClaimAsync();

        claimed.ShouldNotBeNull().Id.ShouldBe(job.Id);
        var stored = await LoadAsync(job.Id);
        stored.Status.ShouldBe(ProcessingJobStatus.Running);
        stored.Attempts.ShouldBe(1);
        stored.LockedUntil.ShouldNotBeNull().ShouldBeInRange(before + Lock, DateTimeOffset.UtcNow + Lock);
        stored.NextAttemptAt.ShouldBeNull();

        (await ClaimAsync()).ShouldBeNull("a locked job is not taken again");
    }

    [Fact]
    public async Task A_crashed_workers_job_is_taken_over_as_a_new_attempt()
    {
        var document = await SeedDocumentAsync();
        var job = Running(document.Id, startedAt: DateTimeOffset.UtcNow - Lock - TimeSpan.FromSeconds(1));
        await SaveAsync([job]);

        (await ClaimAsync()).ShouldNotBeNull().Id.ShouldBe(job.Id);

        var stored = await LoadAsync(job.Id);
        stored.Attempts.ShouldBe(2);
        stored.LockedUntil.ShouldNotBeNull().ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_retry_is_taken_exactly_at_its_next_attempt_time()
    {
        var document = await SeedDocumentAsync();
        var job = Retried(document.Id, failedAt: DateTimeOffset.UtcNow);
        await SaveAsync([job]);

        // Read it back: PostgreSQL stores microseconds, .NET keeps 100 ns ticks.
        var dueAt = (await LoadAsync(job.Id)).NextAttemptAt.ShouldNotBeNull();

        (await ClaimAsync(at: dueAt.AddTicks(-10))).ShouldBeNull("a microsecond early");
        (await ClaimAsync(at: dueAt)).ShouldNotBeNull("at the time").Id.ShouldBe(job.Id);
    }

    [Fact]
    public async Task Jobs_are_taken_oldest_first()
    {
        var document = await SeedDocumentAsync();

        // Ids (UUID v7) are only ordered across milliseconds, so the jobs are created a few ms apart:
        // a < b < c by id. Then they are saved c, b, a, one by one: by time (CreatedAt) c is the oldest.
        var jobs = new List<ProcessingJob>();
        for (var i = 0; i < 3; i++)
        {
            jobs.Add(ProcessingJob.For(document.Id));
            await Task.Delay(5, Ct);
        }

        var (a, b, c) = (jobs[0], jobs[1], jobs[2]);
        a.Id.CompareTo(b.Id).ShouldBeLessThan(0);
        b.Id.CompareTo(c.Id).ShouldBeLessThan(0);
        foreach (var job in new[] { c, b, a })
        {
            await SaveAsync([job]);
        }

        (await ClaimAllAsync()).ShouldBe([c.Id, b.Id, a.Id], "by time, not by id");
    }

    [Fact]
    public async Task Many_workers_at_once_take_every_job_exactly_once()
    {
        var document = await SeedDocumentAsync();
        var jobs = Enumerable.Range(0, 40).Select(_ => ProcessingJob.For(document.Id)).ToList();
        await SaveAsync(jobs);

        // 8 workers race for 40 jobs, each in its own unit of work (its own connection), until none is left.
        var taken = new ConcurrentBag<(int Worker, Guid Job)>();
        using var start = new SemaphoreSlim(0);
        var workers = Enumerable.Range(0, 8).Select(worker => Task.Run(async () =>
        {
            await start.WaitAsync(Ct);
            while (true)
            {
                await using var scope = fixture.OpenScope(tenantId: null);
                var job = await scope.Services.GetRequiredService<IProcessingJobQueue>().ClaimNextAsync(Ct);
                if (job is null)
                {
                    return;
                }

                taken.Add((worker, job.Id));
            }
        }, Ct)).ToList();
        start.Release(8);
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30), Ct);

        taken.Select(t => t.Job).ShouldBe(jobs.Select(j => j.Id), ignoreOrder: true, "every job once, none twice");
        taken.Select(t => t.Worker).Distinct().Count().ShouldBeGreaterThan(1, "the work was really shared");

        await using var check = fixture.OpenScope(tenantId: null);
        var stored = await check.Db.ProcessingJobs.AsNoTracking().ToListAsync(Ct);
        stored.ShouldAllBe(j => j.Status == ProcessingJobStatus.Running && j.Attempts == 1);
    }

    [Fact]
    public async Task A_job_another_worker_is_claiming_is_skipped_not_waited_for()
    {
        var document = await SeedDocumentAsync();
        var first = ProcessingJob.For(document.Id);
        var second = ProcessingJob.For(document.Id);
        await SaveAsync([first]);
        await SaveAsync([second]);

        // Another worker is in the middle of claiming the oldest job: it holds that row's lock.
        await using var other = fixture.OpenScope(tenantId: null);
        await using var otherTransaction = await other.Db.Database.BeginTransactionAsync(Ct);
        await other.Db.Database.ExecuteSqlAsync($"SELECT id FROM processing_jobs WHERE id = {first.Id} FOR UPDATE", Ct);

        // This worker does not wait for that lock: it takes the next job at once.
        var claimed = await ClaimAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);
        claimed.ShouldNotBeNull().Id.ShouldBe(second.Id);

        // Once the other worker lets go (here: without taking it), the first job is available again.
        await otherTransaction.RollbackAsync(Ct);
        (await ClaimAsync()).ShouldNotBeNull().Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task The_row_lock_ends_with_the_claim_not_with_the_job()
    {
        var document = await SeedDocumentAsync();
        var job = ProcessingJob.For(document.Id);
        await SaveAsync([job]);

        // The worker keeps its unit of work open while processing; the row must still be free for others
        // (e.g. a user deleting the document), because only the Running status + lock time guard the job.
        await using var worker = fixture.OpenScope(tenantId: null);
        (await worker.Services.GetRequiredService<IProcessingJobQueue>().ClaimNextAsync(Ct)).ShouldNotBeNull();

        await using var other = fixture.OpenScope(tenantId: null);
        await using var transaction = await other.Db.Database.BeginTransactionAsync(Ct);
        await other.Db.Database.ExecuteSqlAsync($"SELECT id FROM processing_jobs WHERE id = {job.Id} FOR UPDATE NOWAIT", Ct);
        await transaction.RollbackAsync(Ct);
    }

    // ---------- helpers ----------

    /// <summary>One claim in its own unit of work, like the Worker does; optionally at a chosen time.</summary>
    private async Task<ProcessingJob?> ClaimAsync(DateTimeOffset? at = null)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        IProcessingJobQueue queue = at is { } time
            ? new ProcessingJobQueue(
                scope.Db, scope.Services.GetRequiredService<Npgsql.NpgsqlDataSource>(), new FixedTime(time),
                Microsoft.Extensions.Options.Options.Create(new ProcessingOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ProcessingJobQueue>.Instance)
            : scope.Services.GetRequiredService<IProcessingJobQueue>();
        return await queue.ClaimNextAsync(Ct);
    }

    private async Task<List<Guid>> ClaimAllAsync()
    {
        var taken = new List<Guid>();
        while (await ClaimAsync() is { } job)
        {
            taken.Add(job.Id);
            taken.Count.ShouldBeLessThan(100, "claiming never ends: a job is taken twice");
        }

        return taken;
    }

    private async Task<ProcessingJob> LoadAsync(Guid jobId)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        return await scope.Db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == jobId, Ct);
    }

    private static ProcessingJob Retried(Guid documentId, DateTimeOffset failedAt)
    {
        var job = ProcessingJob.For(documentId);
        job.Start(failedAt.AddMinutes(-1), Lock);
        job.Fail("temporary error", failedAt).ShouldBeFalse();
        return job; // Pending again, next attempt 30 s after the failure
    }

    private static ProcessingJob Running(Guid documentId, DateTimeOffset startedAt)
    {
        var job = ProcessingJob.For(documentId);
        job.Start(startedAt, Lock);
        return job;
    }

    private static ProcessingJob Succeeded(Guid documentId, DateTimeOffset now)
    {
        var job = Running(documentId, now.AddMinutes(-1));
        job.Succeed(now);
        return job;
    }

    private static ProcessingJob FailedForGood(Guid documentId, DateTimeOffset now)
    {
        var job = ProcessingJob.For(documentId);
        var time = now.AddDays(-1);
        for (var attempt = 1; attempt <= ProcessingJob.MaxAttempts; attempt++)
        {
            job.Start(time, Lock);
            job.Fail("broken file", time).ShouldBe(attempt == ProcessingJob.MaxAttempts);
            time = time.AddHours(1);
        }

        return job;
    }

    private async Task SaveAsync(IEnumerable<ProcessingJob> jobs)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.ProcessingJobs.AddRange(jobs);
        await scope.Db.SaveChangesAsync(Ct);
    }

    private async Task<Document> SeedDocumentAsync()
    {
        var tenant = Tenant.Create("Jobs company");
        tenant.ChangeSlug($"jobs-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");
        var document = Document.FromUpload(
            tenant.Id, bot.Id, "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenant.Id}/{bot.Id}/{Guid.NewGuid():N}/original.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));

        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.AddRange(tenant, bot, document);
        await scope.Db.SaveChangesAsync(Ct);
        return document;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
