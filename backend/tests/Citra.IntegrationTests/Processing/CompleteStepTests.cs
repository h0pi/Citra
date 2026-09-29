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
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Citra.IntegrationTests.Processing;

/// <summary>
/// C-145: the final step saves, in one transaction, the chunks, the document as Ready with its page and chunk
/// counts, and the job as Succeeded — and saves none of it if the job stopped being this worker's meanwhile.
/// Runs on the isolated database because the runner test takes "the oldest due job" of all.
/// </summary>
[Collection(IsolatedDatabaseCollection.Name)]
public sealed class CompleteStepTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Whole microseconds: PostgreSQL stores no finer, so saved times compare exactly.
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 29, 12, 30, 15, TimeSpan.Zero).AddMicroseconds(123_456));

    public async ValueTask InitializeAsync()
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Db.ProcessingJobs.ExecuteDeleteAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_pdf_becomes_Ready_with_its_page_and_chunk_counts_and_the_job_succeeds()
    {
        var (document, job) = await SeedAsync("Handbook.pdf", DocumentSourceType.Pdf);
        var text = Pages("Vacation: 25 days.", "Sick leave: call before 9 am.", "Remote work: two days a week.");

        var chunkCount = await RunStepsAsync(document.Id, job.Id, text);

        await using var check = fixture.OpenScope(tenantId: null);
        var saved = await LoadAsync(check, document.Id);
        saved.Status.ShouldBe(DocumentStatus.Ready);
        saved.PageCount.ShouldBe(3);
        saved.ChunkCount.ShouldBe(chunkCount);
        saved.ChunkCount.ShouldBe(await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id, Ct));
        saved.ProcessedAt.ShouldBe(_clock.GetUtcNow());
        saved.ErrorMessage.ShouldBeNull();

        var finished = await check.Db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id, Ct);
        finished.Status.ShouldBe(ProcessingJobStatus.Succeeded);
        finished.CompletedAt.ShouldBe(_clock.GetUtcNow());
        finished.LockedUntil.ShouldBeNull();
        finished.Attempts.ShouldBe(1);
    }

    [Theory]
    [InlineData("notes.txt", DocumentSourceType.Text)]
    [InlineData("notes.md", DocumentSourceType.Markdown)]
    public async Task Text_files_count_no_pages(string fileName, DocumentSourceType sourceType)
    {
        var (document, job) = await SeedAsync(fileName, sourceType);

        await RunStepsAsync(document.Id, job.Id, Pages("Employees get 25 days of paid vacation."));

        await using var check = fixture.OpenScope(tenantId: null);
        var saved = await LoadAsync(check, document.Id);
        saved.Status.ShouldBe(DocumentStatus.Ready);
        saved.PageCount.ShouldBe(0, "their chunks have no page numbers either");
        saved.ChunkCount.ShouldBe(1);
    }

    [Fact]
    public async Task Nothing_is_saved_if_another_worker_took_the_job_over_meanwhile()
    {
        var (document, job) = await SeedAsync("Handbook.pdf", DocumentSourceType.Pdf);

        // While this worker embeds, its lock runs out and another worker takes the job (a new attempt).
        using var intercept = fixture.Embeddings.Intercept(async (_, _) =>
        {
            await using var other = fixture.OpenScope(tenantId: null);
            await other.Db.Database.ExecuteSqlAsync($"UPDATE processing_jobs SET attempts = attempts + 1 WHERE id = {job.Id}", Ct);
        });

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => RunStepsAsync(document.Id, job.Id, Pages("Page one.", "Page two.")));

        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id, Ct)).ShouldBe(0, "rolled back with the job");
        (await LoadAsync(check, document.Id)).Status.ShouldBe(DocumentStatus.Embedding, "not Ready");
        var others = await check.Db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id, Ct);
        others.Status.ShouldBe(ProcessingJobStatus.Running, "the other worker's job is untouched");
        others.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Nothing_is_saved_if_the_document_was_deleted_meanwhile()
    {
        var (document, job) = await SeedAsync("Handbook.pdf", DocumentSourceType.Pdf);
        using var intercept = fixture.Embeddings.Intercept(async (_, _) =>
        {
            await using var other = fixture.OpenScope(tenantId: null);
            await other.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]).Where(d => d.Id == document.Id).ExecuteDeleteAsync(Ct);
        });

        // The chunks (and the cost record) point at a document that no longer exists: the database refuses them.
        await Should.ThrowAsync<DbUpdateException>(() => RunStepsAsync(document.Id, job.Id, Pages("Page one.")));

        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id, Ct)).ShouldBe(0);
        (await check.Db.ProcessingJobs.CountAsync(j => j.Id == job.Id, Ct)).ShouldBe(0, "deleted with its document");
    }

    [Fact]
    public async Task The_worker_treats_a_job_lost_at_the_final_save_as_taken_over_not_as_an_error()
    {
        var document = await SeedStoredTextAsync("Employees get 25 days of paid vacation.");
        Guid? jobId = null;
        using var intercept = fixture.Embeddings.Intercept(async (_, _) =>
        {
            await using var other = fixture.OpenScope(tenantId: null);
            jobId = await other.Db.ProcessingJobs.Where(j => j.DocumentId == document.Id).Select(j => j.Id).SingleAsync(Ct);
            await other.Db.Database.ExecuteSqlAsync($"UPDATE processing_jobs SET attempts = attempts + 1 WHERE id = {jobId}", Ct);
        });

        await using (var worker = fixture.OpenScope(tenantId: null))
        {
            (await worker.Services.GetRequiredService<IProcessingJobRunner>().RunNextAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct))
                .ShouldBeTrue("handled: the job is someone else's now");
        }

        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id, Ct)).ShouldBe(0);
        var job = await check.Db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == jobId, Ct);
        job.Status.ShouldBe(ProcessingJobStatus.Running, "not marked failed: it was not this worker's any more");
        job.LastError.ShouldBeNull();
    }

    // ---------- helpers ----------

    private static ExtractedDocument Pages(params string[] pages) =>
        new([.. pages.Select((text, i) => new ExtractedPage(i + 1, text))]);

    /// <summary>Steps 2, 3 and the final save in one unit of work, with the fake clock; returns the chunk count.</summary>
    private async Task<int> RunStepsAsync(Guid documentId, Guid jobId, ExtractedDocument text)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var pipeline = new DocumentPipeline(
            scope.Db, scope.Services.GetRequiredService<ITextExtractorFactory>(), scope.Services.GetRequiredService<TextChunker>(),
            scope.Services.GetRequiredService<IEmbeddingService>(), _clock, NullLogger<DocumentPipeline>.Instance);
        var document = await LoadAsync(scope, documentId, tracked: true);
        var job = await scope.Db.ProcessingJobs.SingleAsync(j => j.Id == jobId, Ct);

        var chunks = await pipeline.ChunkAsync(document, text, Ct);
        var embedded = await pipeline.EmbedAsync(document, chunks, Ct);
        await pipeline.CompleteAsync(job, document, text, embedded, Ct);
        return chunks.Count;
    }

    private static Task<Document> LoadAsync(DbScope scope, Guid documentId, bool tracked = false)
    {
        var documents = scope.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]);
        return (tracked ? documents : documents.AsNoTracking()).SingleAsync(d => d.Id == documentId, Ct);
    }

    /// <summary>A just-extracted document with its running job (attempt 1).</summary>
    private async Task<(Document Document, ProcessingJob Job)> SeedAsync(string fileName, DocumentSourceType sourceType)
    {
        var (tenant, bot) = NewTenantAndBot();
        var document = Document.FromUpload(
            tenant.Id, bot.Id, fileName, "application/octet-stream", 100, sourceType,
            $"{tenant.Id}/{bot.Id}/{Guid.NewGuid():N}/original.bin", new string('a', 64));
        document.StartExtracting();
        var job = ProcessingJob.For(document.Id);
        job.Start(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15));

        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.AddRange(tenant, bot, document, job);
        await scope.Db.SaveChangesAsync(Ct);
        return (document, job);
    }

    /// <summary>A new .txt document (file really stored) with a new, due job.</summary>
    private async Task<Document> SeedStoredTextAsync(string content)
    {
        var (tenant, bot) = NewTenantAndBot();
        var id = Guid.CreateVersion7();
        var path = StoragePaths.OriginalFile(tenant.Id, bot.Id, id, "notes.txt");
        var bytes = Encoding.UTF8.GetBytes(content);
        var document = Document.FromUpload(
            tenant.Id, bot.Id, "notes.txt", "text/plain", bytes.Length, DocumentSourceType.Text, path, new string('a', 64), id);

        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Services.GetRequiredService<IFileStorage>().SaveAsync(path, new MemoryStream(bytes), Ct);
        scope.Db.AddRange(tenant, bot, document, ProcessingJob.For(document.Id));
        await scope.Db.SaveChangesAsync(Ct);
        return document;
    }

    private static (Tenant Tenant, Bot Bot) NewTenantAndBot()
    {
        var tenant = Tenant.Create("Complete company");
        tenant.ChangeSlug($"complete-{Guid.NewGuid():N}");
        return (tenant, Bot.Create(tenant.Id, "Support"));
    }
}
