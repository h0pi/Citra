using Citra.Application.Common.Chunking;
using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Extraction;
using Citra.Application.Common.Persistence;
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
/// Pipeline step 2 — the document is marked Chunking (saved first, together with a web page's new name),
/// the cleaned text is split with the real chunker, and every chunk carries what the embedding step needs.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ChunkStepTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_document_shows_Chunking_while_its_text_is_split()
    {
        var document = await SeedAsync("Handbook.pdf", DocumentSourceType.Pdf, d => d.StartExtracting());
        DocumentStatus? statusSeenByOthers = null;

        await using var scope = fixture.OpenScope(tenantId: null);
        var counter = new SpyTokenCounter(scope.Services.GetRequiredService<ITokenCounter>(), () =>
        {
            // First time the chunker measures text: what the documents page would show right now (the chunker is
            // synchronous, so this look from another connection is too).
            var other = fixture.OpenScope(tenantId: null);
            try
            {
                statusSeenByOthers = StatusAsync(other, document.Id).GetAwaiter().GetResult();
            }
            finally
            {
                other.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
        var pipeline = new DocumentPipeline(
            scope.Db, scope.Services.GetRequiredService<ITextExtractorFactory>(), new TextChunker(counter),
            scope.Services.GetRequiredService<IEmbeddingService>(), TimeProvider.System, NullLogger<DocumentPipeline>.Instance);

        await pipeline.ChunkAsync(await LoadAsync(scope, document.Id), Text("Employees get 25 days of paid vacation."), Ct);

        statusSeenByOthers.ShouldBe(DocumentStatus.Chunking);
    }

    [Fact]
    public async Task Pdf_chunks_get_page_numbers_and_point_at_their_exact_text()
    {
        var document = await SeedAsync("Employee Handbook.pdf", DocumentSourceType.Pdf, d => d.StartExtracting());
        var text = new ExtractedDocument(
        [
            new ExtractedPage(1, "Vacation\n\nEmployees get 25 days of paid vacation."),
            new ExtractedPage(2, "Sick leave\n\nTell your manager before 9 am."),
        ]);

        var chunks = await ChunkAsync(document.Id, text);

        chunks.Select(c => c.PageNumber).ShouldBe([1, 2]);
        chunks.Select(c => c.Index).ShouldBe([0, 1]);
        foreach (var chunk in chunks)
        {
            chunk.Content.ShouldBe(text.Pages[chunk.PageNumber!.Value - 1].Text[chunk.CharStart..chunk.CharEnd]);
            chunk.EmbeddingText.ShouldBe(ChunkContext.ForEmbedding("Employee Handbook", chunk.Heading, chunk.Content));
            chunk.EmbeddingText.ShouldStartWith("Document: Employee Handbook\n");
            chunk.TokenCount.ShouldBeGreaterThan(0);
        }
    }

    [Theory]
    [InlineData("notes.txt", DocumentSourceType.Text)]
    [InlineData("notes.md", DocumentSourceType.Markdown)]
    public async Task Text_files_have_no_page_numbers(string fileName, DocumentSourceType sourceType)
    {
        var document = await SeedAsync(fileName, sourceType, d => d.StartExtracting());

        var chunk = (await ChunkAsync(document.Id, Text("Employees get 25 days of paid vacation."))).ShouldHaveSingleItem();

        chunk.PageNumber.ShouldBeNull();
        chunk.EmbeddingText.ShouldStartWith("Document: notes\n");
    }

    [Fact]
    public async Task A_word_files_title_names_its_chunks_but_the_file_keeps_its_name()
    {
        var document = await SeedAsync("handbook_v3_final.docx", DocumentSourceType.Docx, d => d.StartExtracting());
        var text = new ExtractedDocument([new ExtractedPage(1, "Employees get 25 days of paid vacation.")], Title: "Employee Handbook 2026");

        var chunk = (await ChunkAsync(document.Id, text)).ShouldHaveSingleItem();

        chunk.EmbeddingText.ShouldStartWith("Document: Employee Handbook 2026\n");
        await using var check = fixture.OpenScope(tenantId: null);
        (await LoadAsync(check, document.Id)).FileName.ShouldBe("handbook_v3_final.docx");
    }

    [Fact]
    public async Task A_web_pages_title_is_saved_as_its_name_and_names_its_chunks()
    {
        var document = await SeedUrlAsync("https://example.com/hr/leave");
        var text = new ExtractedDocument([new ExtractedPage(1, "You get 25 days of leave.")], Title: "Leave policy – ACME");

        List<TextChunk> chunks;
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            var pipeline = scope.Services.GetRequiredService<DocumentPipeline>();
            var loaded = await LoadAsync(scope, document.Id);
            loaded.StartExtracting();
            loaded.SetTitle(text.Title!); // what step 1 does for a web page, without fetching it for real
            chunks = [.. await pipeline.ChunkAsync(loaded, text, Ct)];
        }

        chunks.ShouldHaveSingleItem().EmbeddingText.ShouldStartWith("Document: Leave policy – ACME\n");
        await using var check = fixture.OpenScope(tenantId: null);
        (await LoadAsync(check, document.Id)).FileName.ShouldBe("Leave policy – ACME", "step 2 saved the name from step 1");
    }

    [Fact]
    public async Task A_long_document_is_split_into_many_chunks_within_the_size_limit()
    {
        var document = await SeedAsync("Manual.txt", DocumentSourceType.Text, d => d.StartExtracting());
        var paragraphs = Enumerable.Range(1, 120).Select(i =>
            $"Rule {i}. Every employee must follow rule number {i} carefully, because it keeps the warehouse safe and clean.");
        var text = Text(string.Join("\n\n", paragraphs));

        var chunks = await ChunkAsync(document.Id, text);

        chunks.Count.ShouldBeGreaterThan(3);
        chunks.Select(c => c.Index).ShouldBe(Enumerable.Range(0, chunks.Count));
        chunks.ShouldAllBe(c => c.TokenCount <= ChunkingOptions.Default.MaxTokens);
        chunks[0].Content.ShouldStartWith("Rule 1.");
        chunks[^1].Content.ShouldEndWith("Rule 120. Every employee must follow rule number 120 carefully, because it keeps the warehouse safe and clean.");
    }

    [Fact]
    public async Task Chunks_are_not_saved_yet_they_are_saved_with_their_embeddings()
    {
        var document = await SeedAsync("Handbook.pdf", DocumentSourceType.Pdf, d => d.StartExtracting());

        (await ChunkAsync(document.Id, Text("Employees get 25 days of paid vacation."))).ShouldNotBeEmpty();

        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id, Ct)).ShouldBe(0);
        (await StatusAsync(check, document.Id)).ShouldBe(DocumentStatus.Chunking);
    }

    [Fact]
    public async Task A_document_of_only_a_heading_still_gives_a_chunk()
    {
        var document = await SeedAsync("Title.md", DocumentSourceType.Markdown, d => d.StartExtracting());

        var chunk = (await ChunkAsync(document.Id, Text("# Employee Handbook"))).ShouldHaveSingleItem();

        chunk.Content.ShouldBe("# Employee Handbook");
    }

    // ---------- helpers ----------

    private static ExtractedDocument Text(string text) => new([new ExtractedPage(1, text)]);

    private async Task<List<TextChunk>> ChunkAsync(Guid documentId, ExtractedDocument text)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var document = await LoadAsync(scope, documentId);
        return [.. await scope.Services.GetRequiredService<DocumentPipeline>().ChunkAsync(document, text, Ct)];
    }

    private static Task<Document> LoadAsync(DbScope scope, Guid documentId) =>
        scope.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]).SingleAsync(d => d.Id == documentId, Ct);

    private static Task<DocumentStatus> StatusAsync(DbScope scope, Guid documentId) =>
        scope.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(d => d.Id == documentId).Select(d => d.Status).SingleAsync(Ct);

    private async Task<Document> SeedAsync(string fileName, DocumentSourceType sourceType, Action<Document> prepare)
    {
        var (tenant, bot) = NewTenantAndBot();
        var document = Document.FromUpload(
            tenant.Id, bot.Id, fileName, "application/octet-stream", 100, sourceType,
            $"{tenant.Id}/{bot.Id}/{Guid.NewGuid():N}/original.bin", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        prepare(document);
        await SaveAsync(tenant, bot, document);
        return document;
    }

    private async Task<Document> SeedUrlAsync(string url)
    {
        var (tenant, bot) = NewTenantAndBot();
        var document = Document.FromUrl(tenant.Id, bot.Id, new Uri(url));
        await SaveAsync(tenant, bot, document);
        return document;
    }

    private async Task SaveAsync(params object[] entities)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.AddRange(entities);
        await scope.Db.SaveChangesAsync(Ct);
    }

    private static (Tenant Tenant, Bot Bot) NewTenantAndBot()
    {
        var tenant = Tenant.Create("Chunk company");
        tenant.ChangeSlug($"chunk-{Guid.NewGuid():N}");
        return (tenant, Bot.Create(tenant.Id, "Support"));
    }

    /// <summary>The real token counter, calling <paramref name="onFirstUse"/> once before it first counts.</summary>
    private sealed class SpyTokenCounter(ITokenCounter inner, Action onFirstUse) : ITokenCounter
    {
        private bool _used;

        public int Count(string text)
        {
            Touch();
            return inner.Count(text);
        }

        public int IndexAfterTokens(string text, int maxTokens)
        {
            Touch();
            return inner.IndexAfterTokens(text, maxTokens);
        }

        public int IndexBeforeLastTokens(string text, int maxTokens)
        {
            Touch();
            return inner.IndexBeforeLastTokens(text, maxTokens);
        }

        private void Touch()
        {
            if (!_used)
            {
                _used = true;
                onFirstUse();
            }
        }
    }
}
