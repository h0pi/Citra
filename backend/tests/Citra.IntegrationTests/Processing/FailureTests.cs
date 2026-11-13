using System.Text;
using Citra.Application.Common.Chunking;
using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Extraction;
using Citra.Application.Common.Persistence;
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
/// A failed attempt is retried (after 30 s, then 2 min) until 3 attempts are used, then the job and its
/// document are Failed with a message for the user; errors another attempt cannot fix fail at once; and what is
/// not this worker's failure (a lost job, a deleted document, a shutdown) is not recorded as one.
/// The real runner with a fake clock; runs on the isolated database (the runner takes the oldest due job).
/// </summary>
[Collection(IsolatedDatabaseCollection.Name)]
public sealed class FailureTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly EmbeddingException VoyageDown = new("Voyage AI could not be reached.");

    private readonly FakeTimeProvider _clock = new(Microseconds(DateTimeOffset.UtcNow));

    public async ValueTask InitializeAsync()
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Db.ProcessingJobs.ExecuteDeleteAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_temporary_error_is_retried_30_seconds_later_and_can_then_succeed()
    {
        var document = await SeedTextAsync();
        using (fixture.Embeddings.Intercept((_, _) => throw VoyageDown))
        {
            (await RunAsync()).ShouldBeTrue("the failure is handled, not thrown");
        }

        var job = await JobAsync(document.Id);
        job.Status.ShouldBe(ProcessingJobStatus.Pending);
        job.Attempts.ShouldBe(1);
        job.NextAttemptAt.ShouldBe(_clock.GetUtcNow().AddSeconds(30));
        job.LockedUntil.ShouldBeNull();
        job.LastError.ShouldBe("EmbeddingException: Voyage AI could not be reached.");
        var waiting = await DocumentAsync(document.Id);
        waiting.Status.ShouldNotBe(DocumentStatus.Failed, "it is tried again");
        waiting.ErrorMessage.ShouldBeNull();

        (await RunAsync()).ShouldBeFalse("not due before its time");
        _clock.Advance(TimeSpan.FromSeconds(30));
        (await RunAsync()).ShouldBeTrue();

        (await DocumentAsync(document.Id)).Status.ShouldBe(DocumentStatus.Ready);
        var done = await JobAsync(document.Id);
        done.Status.ShouldBe(ProcessingJobStatus.Succeeded);
        done.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task After_three_failed_attempts_the_document_fails_with_a_message_for_the_user()
    {
        var document = await SeedTextAsync();
        using var intercept = fixture.Embeddings.Intercept((_, _) => throw VoyageDown);

        (await RunAsync()).ShouldBeTrue();
        _clock.Advance(TimeSpan.FromSeconds(30));
        (await RunAsync()).ShouldBeTrue();
        (await JobAsync(document.Id)).NextAttemptAt.ShouldBe(_clock.GetUtcNow().AddMinutes(2), "the wait grows: 30 s, then 2 min");
        _clock.Advance(TimeSpan.FromMinutes(2));
        (await RunAsync()).ShouldBeTrue();

        var job = await JobAsync(document.Id);
        job.Status.ShouldBe(ProcessingJobStatus.Failed);
        job.Attempts.ShouldBe(ProcessingJob.MaxAttempts);
        job.CompletedAt.ShouldBe(_clock.GetUtcNow());
        var failed = await DocumentAsync(document.Id);
        failed.Status.ShouldBe(DocumentStatus.Failed);
        failed.ErrorMessage.ShouldBe("The text could not be prepared for search: Voyage AI could not be reached. Try Reprocess later.");
        failed.ProcessedAt.ShouldBe(_clock.GetUtcNow());

        _clock.Advance(TimeSpan.FromHours(1));
        (await RunAsync()).ShouldBeFalse("a failed job is never tried again by itself");
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_fails_at_once_without_retries()
    {
        var document = await SeedTextAsync();
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            await scope.Services.GetRequiredService<IFileStorage>().DeleteAsync(document.StoragePath!, Ct);
        }

        (await RunAsync()).ShouldBeTrue();

        var job = await JobAsync(document.Id);
        job.Status.ShouldBe(ProcessingJobStatus.Failed);
        job.Attempts.ShouldBe(1);
        var failed = await DocumentAsync(document.Id);
        failed.Status.ShouldBe(DocumentStatus.Failed);
        failed.ErrorMessage.ShouldBe("The file of this document is missing. Upload it again.");
    }

    [Fact]
    public async Task A_web_page_that_cannot_be_read_is_retried_the_site_may_be_down_for_a_moment()
    {
        var document = await SeedUrlAsync();
        var extractor = new ThrowingExtractor(new TextExtractionException("The page could not be reached. Check the address."));

        (await RunAsync(extractor)).ShouldBeTrue();

        var job = await JobAsync(document.Id);
        job.Status.ShouldBe(ProcessingJobStatus.Pending);
        job.LastError.ShouldBe("TextExtractionException: The page could not be reached. Check the address.");
        (await DocumentAsync(document.Id)).Status.ShouldNotBe(DocumentStatus.Failed);
    }

    [Fact]
    public async Task An_unexpected_error_shows_a_general_message_and_keeps_every_detail_for_operators()
    {
        var document = await SeedTextAsync(failedAttempts: 2); // the last attempt
        var extractor = new ThrowingExtractor(new InvalidOperationException("boom", new ArgumentException("inner cause")));

        (await RunAsync(extractor)).ShouldBeTrue();

        var job = await JobAsync(document.Id);
        job.Status.ShouldBe(ProcessingJobStatus.Failed);
        job.LastError.ShouldBe("InvalidOperationException: boom → ArgumentException: inner cause");
        (await DocumentAsync(document.Id)).ErrorMessage.ShouldBe(ProcessingFailures.UnexpectedErrorMessage);
    }

    [Fact]
    public async Task A_failure_after_the_chunks_were_prepared_is_recorded_cleanly()
    {
        // The database refuses one of the chunks at the final save: the failed unit of work still holds the
        // unsaved chunks, so the failure must be recorded in a fresh one (or saving it would fail again).
        var document = await SeedTextAsync(content: "Employees get 25 days of paid vacation. BOOM");
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            await scope.Db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION refuse_boom() RETURNS trigger AS $$
                BEGIN IF NEW.content LIKE '%BOOM%' THEN RAISE EXCEPTION 'test: chunk refused'; END IF; RETURN NEW; END $$ LANGUAGE plpgsql;
                CREATE TRIGGER refuse_boom BEFORE INSERT ON chunks FOR EACH ROW EXECUTE FUNCTION refuse_boom();
                """, Ct);
        }

        try
        {
            (await RunAsync()).ShouldBeTrue();
        }
        finally
        {
            await using var scope = fixture.OpenScope(tenantId: null);
            await scope.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_boom ON chunks; DROP FUNCTION refuse_boom();", Ct);
        }

        var job = await JobAsync(document.Id);
        job.Status.ShouldBe(ProcessingJobStatus.Pending, "retried later");
        job.LastError!.ShouldContain("test: chunk refused");
        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id, Ct)).ShouldBe(0);
        (await check.Db.EmbeddingUsages.IgnoreQueryFilters([QueryFilters.Tenant]).CountAsync(u => u.DocumentId == document.Id, Ct))
            .ShouldBe(1, "the embedding was paid for");
    }

    [Fact]
    public async Task A_document_the_worker_keeps_crashing_on_is_given_up_without_running_it_again()
    {
        // Three attempts were taken and never ended (the worker died each time); the last lock has run out.
        var document = await SeedTextAsync(crashedAttempts: ProcessingJob.MaxAttempts);
        var callsBefore = fixture.Embeddings.DocumentCalls.Count;

        (await RunAsync()).ShouldBeTrue();

        fixture.Embeddings.DocumentCalls.Count.ShouldBe(callsBefore, "not processed a 4th time");
        var job = await JobAsync(document.Id);
        job.Status.ShouldBe(ProcessingJobStatus.Failed);
        job.Attempts.ShouldBe(ProcessingJob.MaxAttempts + 1);
        var failed = await DocumentAsync(document.Id);
        failed.Status.ShouldBe(DocumentStatus.Failed);
        failed.ErrorMessage.ShouldBe(ProcessingFailures.KeptStoppingMessage);
    }

    [Fact]
    public async Task A_document_deleted_during_processing_is_no_failure()
    {
        var document = await SeedTextAsync();
        using var intercept = fixture.Embeddings.Intercept(async (_, _) =>
        {
            await using var other = fixture.OpenScope(tenantId: null);
            await other.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]).Where(d => d.Id == document.Id).ExecuteDeleteAsync(Ct);
        });

        (await RunAsync()).ShouldBeTrue("nothing left to do, nothing thrown");

        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.ProcessingJobs.CountAsync(j => j.DocumentId == document.Id, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task An_error_after_another_worker_took_the_job_over_does_not_touch_its_job()
    {
        var document = await SeedTextAsync();
        using var intercept = fixture.Embeddings.Intercept(async (_, _) =>
        {
            await using var other = fixture.OpenScope(tenantId: null);
            await other.Db.Database.ExecuteSqlAsync(
                $"UPDATE processing_jobs SET attempts = attempts + 1 WHERE document_id = {document.Id}", Ct);
            throw VoyageDown;
        });

        (await RunAsync()).ShouldBeTrue();

        var job = await JobAsync(document.Id);
        job.Status.ShouldBe(ProcessingJobStatus.Running, "the other worker's attempt goes on");
        job.Attempts.ShouldBe(2);
        job.LastError.ShouldBeNull();
        (await DocumentAsync(document.Id)).Status.ShouldNotBe(DocumentStatus.Failed);
    }

    [Fact]
    public async Task A_shutdown_during_processing_is_no_failure()
    {
        var document = await SeedTextAsync();
        var embedding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var intercept = fixture.Embeddings.Intercept(async (_, ct) =>
        {
            embedding.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        await using var scope = fixture.OpenScope(tenantId: null);
        var running = Runner(scope).RunNextAsync(shutdown.Token);
        await embedding.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await shutdown.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        var job = await JobAsync(document.Id);
        job.Status.ShouldBe(ProcessingJobStatus.Running, "it keeps its lock and is taken up again after a restart");
        job.Attempts.ShouldBe(1);
        job.LastError.ShouldBeNull();
    }

    // ---------- helpers ----------

    private static DateTimeOffset Microseconds(DateTimeOffset time) => new(time.Ticks - (time.Ticks % 10), TimeSpan.Zero);

    private async Task<bool> RunAsync(ITextExtractor? extractor = null)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        return await Runner(scope, extractor).RunNextAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    /// <summary>The real runner, pipeline, queue and failure handling, all on the fake clock.</summary>
    private ProcessingJobRunner Runner(DbScope scope, ITextExtractor? extractor = null)
    {
        var services = scope.Services;
        var queue = new ProcessingJobQueue(
            scope.Db, services.GetRequiredService<NpgsqlDataSource>(), _clock,
            Microsoft.Extensions.Options.Options.Create(new ProcessingOptions()), NullLogger<ProcessingJobQueue>.Instance);
        var extractors = extractor is null ? services.GetRequiredService<ITextExtractorFactory>() : new OneExtractor(extractor);
        var pipeline = new DocumentPipeline(
            scope.Db, extractors, services.GetRequiredService<TextChunker>(), services.GetRequiredService<IEmbeddingService>(),
            _clock, NullLogger<DocumentPipeline>.Instance);
        var failures = new ProcessingFailures(services.GetRequiredService<IServiceScopeFactory>(), _clock, NullLogger<ProcessingFailures>.Instance);
        return new ProcessingJobRunner(queue, pipeline, failures, NullLogger<ProcessingJobRunner>.Instance);
    }

    private async Task<ProcessingJob> JobAsync(Guid documentId)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        return await scope.Db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.DocumentId == documentId, Ct);
    }

    private async Task<Document> DocumentAsync(Guid documentId)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        return await scope.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking().SingleAsync(d => d.Id == documentId, Ct);
    }

    /// <summary>
    /// A .txt document (file really stored) with its job: new, or with earlier attempts that failed normally
    /// (due now) or that never ended (worker crashed; lock run out).
    /// </summary>
    private async Task<Document> SeedTextAsync(
        string content = "Employees get 25 days of paid vacation.", int failedAttempts = 0, int crashedAttempts = 0)
    {
        var (tenant, bot) = NewTenantAndBot();
        var id = Guid.CreateVersion7();
        var path = StoragePaths.OriginalFile(tenant.Id, bot.Id, id, "notes.txt");
        var bytes = Encoding.UTF8.GetBytes(content);
        var document = Document.FromUpload(
            tenant.Id, bot.Id, "notes.txt", "text/plain", bytes.Length, DocumentSourceType.Text, path, new string('a', 64), id);

        var job = ProcessingJob.For(document.Id);
        var past = _clock.GetUtcNow().AddDays(-1);
        for (var i = 0; i < failedAttempts; i++)
        {
            job.Start(past, TimeSpan.FromMinutes(15));
            job.Fail("earlier failure", past).ShouldBeFalse();
            past = past.AddHours(1);
        }

        for (var i = 0; i < crashedAttempts; i++)
        {
            job.Start(past, TimeSpan.FromMinutes(15)); // never finished: the lock simply ran out
            past = past.AddHours(1);
        }

        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Services.GetRequiredService<IFileStorage>().SaveAsync(path, new MemoryStream(bytes), Ct);
        scope.Db.AddRange(tenant, bot, document, job);
        await scope.Db.SaveChangesAsync(Ct);
        return document;
    }

    private async Task<Document> SeedUrlAsync()
    {
        var (tenant, bot) = NewTenantAndBot();
        var document = Document.FromUrl(tenant.Id, bot.Id, new Uri("https://example.com/help"));

        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.AddRange(tenant, bot, document, ProcessingJob.For(document.Id));
        await scope.Db.SaveChangesAsync(Ct);
        return document;
    }

    private static (Tenant Tenant, Bot Bot) NewTenantAndBot()
    {
        var tenant = Tenant.Create("Failure company");
        tenant.ChangeSlug($"failure-{Guid.NewGuid():N}");
        return (tenant, Bot.Create(tenant.Id, "Support"));
    }

    private sealed class ThrowingExtractor(Exception error) : ITextExtractor
    {
        public IReadOnlyCollection<DocumentSourceType> SourceTypes { get; } = Enum.GetValues<DocumentSourceType>();

        public Task<ExtractedDocument> ExtractAsync(Document document, CancellationToken cancellationToken) => throw error;
    }

    private sealed class OneExtractor(ITextExtractor extractor) : ITextExtractorFactory
    {
        public ITextExtractor For(DocumentSourceType sourceType) => extractor;
    }
}
