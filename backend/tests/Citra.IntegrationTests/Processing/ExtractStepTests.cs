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

namespace Citra.IntegrationTests.Processing;

/// <summary>
/// C-142: pipeline step 1 — the document is marked Extracting (saved before the slow part starts), its text is
/// read with the real extractors and cleaned, and unreadable documents fail with a message for the user.
/// Runs on the isolated database because the runner tests take "the oldest due job" of all.
/// </summary>
[Collection(IsolatedDatabaseCollection.Name)]
public sealed class ExtractStepTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Db.ProcessingJobs.ExecuteDeleteAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task The_document_shows_Extracting_while_its_text_is_read()
    {
        var document = await SeedTextDocumentAsync("Employees get 25 days of paid vacation.");
        DocumentStatus? statusSeenByOthers = null;
        var extractor = new FakeExtractor(async () =>
        {
            // What the documents page would show right now (another connection).
            await using var other = fixture.OpenScope(tenantId: null);
            statusSeenByOthers = await StatusAsync(other, document.Id);
            return new ExtractedDocument([new ExtractedPage(1, "Employees get 25 days of paid vacation.")]);
        });

        await using var scope = fixture.OpenScope(tenantId: null);
        await Pipeline(scope, extractor).ExtractAsync(await LoadAsync(scope, document.Id), Ct);

        statusSeenByOthers.ShouldBe(DocumentStatus.Extracting);
    }

    [Fact]
    public async Task A_real_file_is_read_and_cleaned()
    {
        // Windows line endings, a soft hyphen and a zero-width space, as copy-pasted text often has.
        const string raw = "Vacation policy\r\n\r\nEmployees get 25 days of paid va­cation.​\r\n";
        var document = await SeedTextDocumentAsync(raw);

        await using var scope = fixture.OpenScope(tenantId: null);
        var text = await scope.Services.GetRequiredService<DocumentPipeline>()
            .ExtractAsync(await LoadAsync(scope, document.Id), Ct);

        var page = text.Pages.ShouldHaveSingleItem();
        page.PageNumber.ShouldBe(1);
        page.Text.ShouldBe(TextCleaner.Clean(new ExtractedDocument([new ExtractedPage(1, raw)])).Pages[0].Text);
        page.Text.ShouldContain("Employees get 25 days of paid vacation.");
        page.Text.ShouldNotContain('\r');
        page.Text.ShouldNotContain('​');
        (await StatusAsync(scope, document.Id)).ShouldBe(DocumentStatus.Extracting);
    }

    [Fact]
    public async Task A_web_pages_title_becomes_the_document_name()
    {
        var document = await SeedUrlDocumentAsync("https://example.com/hr/leave");
        var extractor = new FakeExtractor(() => Task.FromResult(new ExtractedDocument(
            [new ExtractedPage(1, "You get 25 days of leave.")], Title: "  Leave policy – ACME  ")));

        await using var scope = fixture.OpenScope(tenantId: null);
        var loaded = await LoadAsync(scope, document.Id);
        loaded.FileName.ShouldBe("example.com/hr/leave");

        await Pipeline(scope, extractor).ExtractAsync(loaded, Ct);

        loaded.FileName.ShouldBe("Leave policy – ACME");
    }

    [Theory]
    [InlineData(DocumentSourceType.Pdf)]
    [InlineData(DocumentSourceType.Docx)]
    [InlineData(DocumentSourceType.Text)]
    public async Task An_uploaded_file_keeps_its_file_name_whatever_title_it_contains(DocumentSourceType sourceType)
    {
        // Word files carry a built-in title that is often a leftover ("Document1"); downloads need the real name.
        var document = await SeedTextDocumentAsync("Text.", sourceType: sourceType);
        var extractor = new FakeExtractor(() => Task.FromResult(new ExtractedDocument(
            [new ExtractedPage(1, "Employees get 25 days of paid vacation.")], Title: "Document1")));

        await using var scope = fixture.OpenScope(tenantId: null);
        var loaded = await LoadAsync(scope, document.Id);
        await Pipeline(scope, extractor).ExtractAsync(loaded, Ct);

        loaded.FileName.ShouldBe("notes.txt");
    }

    [Theory]
    [InlineData("   \n\n\t  ")]
    [InlineData("​​ ­\r\n")]
    public async Task A_document_without_readable_text_fails_with_a_message_for_the_user(string content)
    {
        var document = await SeedTextDocumentAsync(content);

        await using var scope = fixture.OpenScope(tenantId: null);
        var loaded = await LoadAsync(scope, document.Id);
        var error = await Should.ThrowAsync<TextExtractionException>(() =>
            scope.Services.GetRequiredService<DocumentPipeline>().ExtractAsync(loaded, Ct));

        error.Message.ShouldBe(DocumentPipeline.NoTextMessage);
    }

    [Fact]
    public async Task An_unreadable_file_fails_with_the_extractors_message()
    {
        var document = await SeedTextDocumentAsync("Some text.");
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            await scope.Services.GetRequiredService<IFileStorage>().DeleteAsync(document.StoragePath!, Ct);
        }

        await using var worker = fixture.OpenScope(tenantId: null);
        var loaded = await LoadAsync(worker, document.Id);
        var error = await Should.ThrowAsync<TextExtractionException>(() =>
            worker.Services.GetRequiredService<DocumentPipeline>().ExtractAsync(loaded, Ct));

        error.Message.ShouldBe("The file of this document is missing. Upload it again.");
        (await StatusAsync(worker, document.Id)).ShouldBe(DocumentStatus.Extracting, "failing the document comes with the error handling step");
    }

    [Fact]
    public async Task The_worker_takes_a_job_and_runs_the_pipeline_steps_so_far()
    {
        var document = await SeedTextDocumentAsync("Employees get 25 days of paid vacation.");
        var job = ProcessingJob.For(document.Id);
        await SaveAsync(job);

        // As in the Worker: nobody is logged in, yet the tenant's document is found and processed.
        await using (var worker = fixture.OpenScope(tenantId: null))
        {
            (await worker.Services.GetRequiredService<IProcessingJobRunner>().RunNextAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct)).ShouldBeTrue();
        }

        await using (var worker = fixture.OpenScope(tenantId: null))
        {
            (await worker.Services.GetRequiredService<IProcessingJobRunner>().RunNextAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct)).ShouldBeFalse("no job left");
        }

        await using var check = fixture.OpenScope(tenantId: null);
        (await StatusAsync(check, document.Id)).ShouldBe(DocumentStatus.Ready, "extracted, chunked, embedded, finished");
        (await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id && c.Embedding != null, Ct)).ShouldBe(1);
        (await check.Db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id, Ct)).Status.ShouldBe(ProcessingJobStatus.Succeeded);
    }

    [Fact]
    public async Task A_job_taken_over_after_a_crash_runs_again_from_the_first_step()
    {
        // The crashed worker had got as far as chunking; its lock has expired.
        var document = await SeedTextDocumentAsync("Employees get 25 days of paid vacation.", d =>
        {
            d.StartExtracting();
            d.StartChunking();
        });
        var job = ProcessingJob.For(document.Id);
        job.Start(DateTimeOffset.UtcNow.AddHours(-1), TimeSpan.FromMinutes(15));
        await SaveAsync(job);

        await using (var worker = fixture.OpenScope(tenantId: null))
        {
            (await worker.Services.GetRequiredService<IProcessingJobRunner>().RunNextAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct)).ShouldBeTrue();
        }

        await using var check = fixture.OpenScope(tenantId: null);
        (await StatusAsync(check, document.Id)).ShouldBe(DocumentStatus.Ready, "Chunking → Extracting → … → Ready");
        (await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id, Ct)).ShouldBe(1);
        (await check.Db.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id, Ct)).Attempts.ShouldBe(2);
    }

    // ---------- helpers ----------

    private static DocumentPipeline Pipeline(DbScope scope, ITextExtractor extractor) =>
        new(
            scope.Db, new FakeFactory(extractor), scope.Services.GetRequiredService<TextChunker>(),
            scope.Services.GetRequiredService<IEmbeddingService>(), TimeProvider.System, NullLogger<DocumentPipeline>.Instance);

    private static Task<Document> LoadAsync(DbScope scope, Guid documentId) =>
        scope.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]).SingleAsync(d => d.Id == documentId, Ct);

    private static Task<DocumentStatus> StatusAsync(DbScope scope, Guid documentId) =>
        scope.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(d => d.Id == documentId).Select(d => d.Status).SingleAsync(Ct);

    private async Task SaveAsync(ProcessingJob job)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.ProcessingJobs.Add(job);
        await scope.Db.SaveChangesAsync(Ct);
    }

    /// <summary>A tenant's .txt document whose file really is in storage.</summary>
    private async Task<Document> SeedTextDocumentAsync(
        string content, Action<Document>? prepare = null, DocumentSourceType sourceType = DocumentSourceType.Text)
    {
        var (tenant, bot) = NewTenantAndBot();
        var id = Guid.CreateVersion7();
        var path = StoragePaths.OriginalFile(tenant.Id, bot.Id, id, "notes.txt");
        var bytes = Encoding.UTF8.GetBytes(content);
        var document = Document.FromUpload(
            tenant.Id, bot.Id, "notes.txt", "text/plain", bytes.Length, sourceType,
            path, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), id);
        prepare?.Invoke(document);

        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Services.GetRequiredService<IFileStorage>().SaveAsync(path, new MemoryStream(bytes), Ct);
        scope.Db.AddRange(tenant, bot, document);
        await scope.Db.SaveChangesAsync(Ct);
        return document;
    }

    private async Task<Document> SeedUrlDocumentAsync(string url)
    {
        var (tenant, bot) = NewTenantAndBot();
        var document = Document.FromUrl(tenant.Id, bot.Id, new Uri(url));

        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.AddRange(tenant, bot, document);
        await scope.Db.SaveChangesAsync(Ct);
        return document;
    }

    private static (Tenant Tenant, Bot Bot) NewTenantAndBot()
    {
        var tenant = Tenant.Create("Pipeline company");
        tenant.ChangeSlug($"pipeline-{Guid.NewGuid():N}");
        return (tenant, Bot.Create(tenant.Id, "Support"));
    }

    private sealed class FakeExtractor(Func<Task<ExtractedDocument>> extract) : ITextExtractor
    {
        public IReadOnlyCollection<DocumentSourceType> SourceTypes { get; } = Enum.GetValues<DocumentSourceType>();

        public Task<ExtractedDocument> ExtractAsync(Document document, CancellationToken cancellationToken) => extract();
    }

    private sealed class FakeFactory(ITextExtractor extractor) : ITextExtractorFactory
    {
        public ITextExtractor For(DocumentSourceType sourceType) => extractor;
    }
}
