using System.Diagnostics;
using System.Text;
using Citra.Application.Common.Storage;
using Citra.Application.Features.Documents.Processing;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Infrastructure.Options;
using Citra.Infrastructure.Processing;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Citra.IntegrationTests.Processing;

/// <summary>
/// C-144: a taken job's lock is renewed while it is processed (every third of the lock duration), so a slow
/// document is never taken over by a second worker; and if the job does stop being this worker's, processing is
/// cancelled instead of finished twice. A fake clock moves time forward instantly.
/// Runs on the isolated database because the runner takes "the oldest due job" of all.
/// </summary>
[Collection(IsolatedDatabaseCollection.Name)]
public sealed class LockHoldTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ProcessingOptions Settings = new(); // 15-minute lock → renewed every 5 minutes

    private static readonly TimeSpan RenewEvery = Settings.LockDuration / 3;

    private readonly CountingClock _clock = new(DateTimeOffset.UtcNow);

    public async ValueTask InitializeAsync()
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Db.ProcessingJobs.ExecuteDeleteAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task The_lock_is_renewed_every_third_of_its_duration_until_processing_ends()
    {
        var job = await SeedClaimedJobAsync();
        var start = _clock.GetUtcNow();
        (await LockedUntilAsync(job.Id)).ShouldBe(Truncate(start + Settings.LockDuration));

        await using var scope = fixture.OpenScope(tenantId: null);
        using var stop = new CancellationTokenSource();
        var holding = Queue(scope).HoldAsync(job, stop.Token);

        await AdvanceAsync(timersBefore: 1);
        await WaitForLockAsync(job.Id, start + RenewEvery + Settings.LockDuration);
        await AdvanceAsync(timersBefore: 2);
        await WaitForLockAsync(job.Id, start + (2 * RenewEvery) + Settings.LockDuration);

        // Another worker looking after the ORIGINAL lock would have expired finds nothing to take.
        _clock.Advance(Settings.LockDuration - (2 * RenewEvery) + TimeSpan.FromMinutes(1)); // start + 16 min
        await using (var other = fixture.OpenScope(tenantId: null))
        {
            (await Queue(other).ClaimNextAsync(Ct)).ShouldBeNull("the renewed lock still holds");
        }

        await stop.CancelAsync();
        (await holding.WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBeTrue("stopped normally, still the job's owner");
    }

    [Fact]
    public async Task Holding_ends_when_another_worker_has_taken_the_job_over()
    {
        var job = await SeedClaimedJobAsync();

        await using var scope = fixture.OpenScope(tenantId: null);
        var holding = Queue(scope).HoldAsync(job, Ct);
        await TakeOverAsync(job.Id); // e.g. this worker was stuck past its lock and another one took the job
        var othersLock = await LockedUntilAsync(job.Id);

        await AdvanceAsync(timersBefore: 1);

        (await holding.WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBeFalse();
        (await LockedUntilAsync(job.Id)).ShouldBe(othersLock, "the other worker's lock is not touched");
    }

    [Fact]
    public async Task Holding_ends_when_the_job_was_deleted()
    {
        var job = await SeedClaimedJobAsync();

        await using var scope = fixture.OpenScope(tenantId: null);
        var holding = Queue(scope).HoldAsync(job, Ct);
        await using (var other = fixture.OpenScope(tenantId: null))
        {
            await other.Db.ProcessingJobs.Where(j => j.Id == job.Id).ExecuteDeleteAsync(Ct); // document deleted meanwhile
        }

        await AdvanceAsync(timersBefore: 1);

        (await holding.WaitAsync(TimeSpan.FromSeconds(5), Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_slow_document_keeps_its_lock_and_is_finished_by_its_worker()
    {
        var (document, _) = await SeedDocumentWithJobAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var embedding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var intercept = fixture.Embeddings.Intercept(async (_, ct) =>
        {
            embedding.TrySetResult();
            await release.Task.WaitAsync(ct); // a slow embedding plan
        });

        await using var scope = fixture.OpenScope(tenantId: null);
        var running = Runner(scope).RunNextAsync(Ct);
        await embedding.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var start = _clock.GetUtcNow();

        await AdvanceAsync(timersBefore: 1);
        var jobId = await JobIdAsync(document.Id);
        await WaitForLockAsync(jobId, start + RenewEvery + Settings.LockDuration);

        release.SetResult();
        (await running.WaitAsync(TimeSpan.FromSeconds(10), Ct)).ShouldBeTrue();
        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Losing_the_job_cancels_processing_and_saves_nothing()
    {
        var (document, _) = await SeedDocumentWithJobAsync();
        var embedding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        using var intercept = fixture.Embeddings.Intercept(async (_, ct) =>
        {
            embedding.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }
        });

        await using var scope = fixture.OpenScope(tenantId: null);
        var running = Runner(scope).RunNextAsync(Ct);
        await embedding.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        await TakeOverAsync(await JobIdAsync(document.Id));
        await AdvanceAsync(timersBefore: 1);

        (await running.WaitAsync(TimeSpan.FromSeconds(10), Ct)).ShouldBeTrue("the job was handled (by someone else now)");
        cancelled.ShouldBeTrue("this worker stopped instead of finishing a job that is not its own");
        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task After_processing_the_lock_is_no_longer_renewed()
    {
        var (document, _) = await SeedDocumentWithJobAsync();

        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            (await Runner(scope).RunNextAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct)).ShouldBeTrue();
        }

        var jobId = await JobIdAsync(document.Id);
        var lockAfterRun = await LockedUntilAsync(jobId);
        var timers = _clock.TimersCreated;
        _clock.Advance(RenewEvery * 3);
        await Task.Delay(300, Ct);

        (await LockedUntilAsync(jobId)).ShouldBe(lockAfterRun);
        _clock.TimersCreated.ShouldBe(timers, "no heartbeat is left running");
    }

    // ---------- helpers ----------

    private ProcessingJobQueue Queue(DbScope scope) => new(
        scope.Db, scope.Services.GetRequiredService<NpgsqlDataSource>(), _clock,
        Microsoft.Extensions.Options.Options.Create(Settings), NullLogger<ProcessingJobQueue>.Instance);

    private ProcessingJobRunner Runner(DbScope scope) => new(
        Queue(scope), scope.Services.GetRequiredService<DocumentPipeline>(), scope.Services.GetRequiredService<ProcessingFailures>(),
        NullLogger<ProcessingJobRunner>.Instance);

    /// <summary>Moves the clock one renewal interval on, once the heartbeat's next wait exists (else the jump is lost).</summary>
    private async Task AdvanceAsync(int timersBefore)
    {
        var waited = Stopwatch.StartNew();
        while (_clock.TimersCreated < timersBefore)
        {
            waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "the heartbeat never started waiting");
            await Task.Delay(10, Ct);
        }

        _clock.Advance(RenewEvery);
    }

    private async Task WaitForLockAsync(Guid jobId, DateTimeOffset expected)
    {
        var waited = Stopwatch.StartNew();
        while (await LockedUntilAsync(jobId) != Truncate(expected))
        {
            waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), $"lock never moved to {expected:O}");
            await Task.Delay(10, Ct);
        }
    }

    /// <summary>PostgreSQL keeps microseconds.</summary>
    private static DateTimeOffset Truncate(DateTimeOffset time) => new(time.Ticks - (time.Ticks % 10), time.Offset);

    private async Task<DateTimeOffset?> LockedUntilAsync(Guid jobId)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        return await scope.Db.ProcessingJobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.LockedUntil).SingleAsync(Ct);
    }

    private async Task<Guid> JobIdAsync(Guid documentId)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        return await scope.Db.ProcessingJobs.Where(j => j.DocumentId == documentId).Select(j => j.Id).SingleAsync(Ct);
    }

    /// <summary>What another worker does when it takes a job over: a new attempt with its own lock.</summary>
    private async Task TakeOverAsync(Guid jobId)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Db.Database.ExecuteSqlAsync(
            $"UPDATE processing_jobs SET attempts = attempts + 1, locked_until = locked_until + interval '1 hour' WHERE id = {jobId}", Ct);
    }

    private async Task<ProcessingJob> SeedClaimedJobAsync()
    {
        var (_, job) = await SeedDocumentWithJobAsync();
        await using var scope = fixture.OpenScope(tenantId: null);
        var claimed = await Queue(scope).ClaimNextAsync(Ct);
        claimed.ShouldNotBeNull().Id.ShouldBe(job.Id);
        return claimed;
    }

    /// <summary>A tenant's .txt document (file really stored) with a new job.</summary>
    private async Task<(Document Document, ProcessingJob Job)> SeedDocumentWithJobAsync()
    {
        var tenant = Tenant.Create("Lock company");
        tenant.ChangeSlug($"lock-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");
        var id = Guid.CreateVersion7();
        var path = StoragePaths.OriginalFile(tenant.Id, bot.Id, id, "notes.txt");
        var bytes = Encoding.UTF8.GetBytes("Employees get 25 days of paid vacation.");
        var document = Document.FromUpload(
            tenant.Id, bot.Id, "notes.txt", "text/plain", bytes.Length, DocumentSourceType.Text, path, new string('a', 64), id);
        var job = ProcessingJob.For(document.Id);

        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Services.GetRequiredService<IFileStorage>().SaveAsync(path, new MemoryStream(bytes), Ct);
        scope.Db.AddRange(tenant, bot, document, job);
        await scope.Db.SaveChangesAsync(Ct);
        return (document, job);
    }

    /// <summary>A fake clock that counts the timers created on it (every Task.Delay is one).</summary>
    private sealed class CountingClock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private int _timers;

        public int TimersCreated => Volatile.Read(ref _timers);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _timers);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
