using Citra.Application.Common.Chunking;
using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Extraction;
using Citra.Application.Common.Persistence;
using Citra.Application.Features.Documents.Processing;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Usage;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Citra.IntegrationTests.Processing;

/// <summary>
/// C-144: pipeline step 3 — the document is marked Embedding (saved first), every chunk's embedding text is
/// embedded, the cost is recorded, and the chunks are saved with their vectors in one transaction (by the final
/// step): all or none. The embedding provider is <see cref="FakeEmbeddingService"/> (no network, no cost).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class EmbedStepTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_document_shows_Embedding_while_the_provider_works()
    {
        var document = await SeedChunkingDocumentAsync();
        DocumentStatus? statusSeenByOthers = null;
        using var intercept = fixture.Embeddings.Intercept(async (_, _) =>
        {
            await using var other = fixture.OpenScope(tenantId: null);
            statusSeenByOthers = await StatusAsync(other, document.Id);
        });

        await EmbedAsync(document.Id, Pages("Employees get 25 days of paid vacation."));

        statusSeenByOthers.ShouldBe(DocumentStatus.Embedding);
    }

    [Fact]
    public async Task Every_chunk_is_saved_with_its_own_vector_and_position()
    {
        var document = await SeedChunkingDocumentAsync();
        var marker = Guid.NewGuid().ToString("N");
        var text = new ExtractedDocument(
        [
            new ExtractedPage(1, $"# Vacation\n\nEmployees get 25 days of paid vacation. {marker}"),
            new ExtractedPage(2, $"# Sick leave\n\nTell your manager before 9 am. {marker}"),
            new ExtractedPage(3, $"Remote work is allowed two days per week. {marker}"),
        ]);

        var chunks = await EmbedAsync(document.Id, text);

        // The provider got exactly the chunks' embedding texts, in order, in one call.
        fixture.Embeddings.DocumentCalls.Last().ShouldBe(chunks.Select(c => c.EmbeddingText));

        await using var check = fixture.OpenScope(tenantId: null);
        var saved = await check.Db.Chunks.AsNoTracking().Where(c => c.DocumentId == document.Id).OrderBy(c => c.Index).ToListAsync(Ct);
        saved.Count.ShouldBe(3);
        for (var i = 0; i < chunks.Count; i++)
        {
            var (expected, actual) = (chunks[i], saved[i]);
            actual.Index.ShouldBe(expected.Index);
            actual.BotId.ShouldBe(document.BotId);
            actual.Content.ShouldBe(expected.Content);
            actual.Heading.ShouldBe(expected.Heading);
            actual.PageNumber.ShouldBe(expected.PageNumber);
            actual.CharStart.ShouldBe(expected.CharStart);
            actual.CharEnd.ShouldBe(expected.CharEnd);
            actual.TokenCount.ShouldBe(expected.TokenCount);
            actual.Embedding.ShouldBe(FakeEmbeddingService.VectorFor(expected.EmbeddingText), $"chunk {i} has its own vector");
        }

        saved.Select(c => c.PageNumber).ShouldBe([1, 2, 3]);
        (await StatusAsync(check, document.Id)).ShouldBe(DocumentStatus.Ready);
    }

    [Fact]
    public async Task The_cost_is_recorded_for_the_document()
    {
        var document = await SeedChunkingDocumentAsync();

        var chunks = await EmbedAsync(document.Id, Pages("Employees get 25 days of paid vacation.", "Tell your manager before 9 am."));

        await using var check = fixture.OpenScope(tenantId: null);
        var usage = (await check.Db.EmbeddingUsages.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(u => u.DocumentId == document.Id).ToListAsync(Ct)).ShouldHaveSingleItem();
        var tokens = chunks.Sum(c => FakeEmbeddingService.TokensFor(c.EmbeddingText));
        usage.TenantId.ShouldBe(document.TenantId);
        usage.BotId.ShouldBe(document.BotId);
        usage.Purpose.ShouldBe(EmbeddingPurpose.Document);
        usage.Model.ShouldBe(FakeEmbeddingService.ModelName);
        usage.Tokens.ShouldBe(tokens);
        usage.CostUsd.ShouldBe(EmbeddingCost.Calculate(tokens, 0.06m));
    }

    [Fact]
    public async Task When_the_provider_fails_nothing_is_saved()
    {
        var document = await SeedChunkingDocumentAsync();
        using var intercept = fixture.Embeddings.Intercept((_, _) => throw new EmbeddingException("Voyage AI could not be reached."));

        var error = await Should.ThrowAsync<EmbeddingException>(() => EmbedAsync(document.Id, Pages("Employees get 25 days.")));

        error.Message.ShouldBe("Voyage AI could not be reached.");
        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id, Ct)).ShouldBe(0);
        (await check.Db.EmbeddingUsages.IgnoreQueryFilters([QueryFilters.Tenant]).CountAsync(u => u.DocumentId == document.Id, Ct)).ShouldBe(0);
        (await StatusAsync(check, document.Id)).ShouldBe(DocumentStatus.Embedding);
    }

    [Fact]
    public async Task A_wrong_number_of_vectors_is_refused_before_anything_is_saved()
    {
        var document = await SeedChunkingDocumentAsync();

        await using var scope = fixture.OpenScope(tenantId: null);
        var pipeline = new DocumentPipeline(
            scope.Db, scope.Services.GetRequiredService<ITextExtractorFactory>(), scope.Services.GetRequiredService<TextChunker>(),
            new OneVectorShort(), TimeProvider.System, NullLogger<DocumentPipeline>.Instance);
        var loaded = await LoadAsync(scope, document.Id);
        var chunks = await pipeline.ChunkAsync(loaded, Pages("Page one text.", "Page two text."), Ct);

        var error = await Should.ThrowAsync<EmbeddingException>(() => pipeline.EmbedAsync(loaded, chunks, Ct));

        error.Message.ShouldContain("1 vectors for 2 texts");
        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id, Ct)).ShouldBe(0);
        (await check.Db.EmbeddingUsages.IgnoreQueryFilters([QueryFilters.Tenant]).CountAsync(u => u.DocumentId == document.Id, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Chunks_are_saved_all_or_none_but_the_paid_cost_is_kept()
    {
        var document = await SeedChunkingDocumentAsync();

        // The document still has an old chunk (as before a reprocess), and the database refuses the 3rd new chunk.
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            scope.Db.Chunks.Add(Chunk.Create(document.Id, document.BotId, 0, "old", null, 1, 0, 3, 1));
            await scope.Db.SaveChangesAsync(Ct);
            await scope.Db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION refuse_boom_embed() RETURNS trigger AS $$
                BEGIN IF NEW.content LIKE '%BOOM%' THEN RAISE EXCEPTION 'test: chunk refused'; END IF; RETURN NEW; END $$ LANGUAGE plpgsql;
                CREATE TRIGGER refuse_boom_embed BEFORE INSERT ON chunks FOR EACH ROW EXECUTE FUNCTION refuse_boom_embed();
                """, Ct);
        }

        try
        {
            await Should.ThrowAsync<DbUpdateException>(() =>
                EmbedAsync(document.Id, Pages("Page one text.", "Page two text.", "Page three BOOM.")));
        }
        finally
        {
            await using var scope = fixture.OpenScope(tenantId: null);
            await scope.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_boom_embed ON chunks; DROP FUNCTION refuse_boom_embed();", Ct);
        }

        await using var check = fixture.OpenScope(tenantId: null);
        var left = await check.Db.Chunks.AsNoTracking().Where(c => c.DocumentId == document.Id).ToListAsync(Ct);
        left.ShouldHaveSingleItem().Content.ShouldBe(
            "old", "new chunks 1 and 2 rolled back with chunk 3, and the removal of the old chunk with them");
        (await StatusAsync(check, document.Id)).ShouldBe(DocumentStatus.Embedding, "not Ready");
        (await check.Db.EmbeddingUsages.IgnoreQueryFilters([QueryFilters.Tenant]).CountAsync(u => u.DocumentId == document.Id, Ct))
            .ShouldBe(1, "the provider was paid, so the cost stays recorded");
    }

    [Fact]
    public async Task A_big_document_is_saved_completely()
    {
        var document = await SeedChunkingDocumentAsync();
        var pages = Enumerable.Range(1, 250).Select(i => $"Rule {i}: every employee follows rule number {i} carefully.").ToArray();

        var chunks = await EmbedAsync(document.Id, Pages(pages));

        chunks.Count.ShouldBe(250);
        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.Chunks.CountAsync(c => c.DocumentId == document.Id && c.Embedding != null, Ct)).ShouldBe(250);
        (await check.Db.Chunks.Where(c => c.DocumentId == document.Id).Select(c => c.Index).OrderBy(i => i).ToListAsync(Ct))
            .ShouldBe(Enumerable.Range(0, 250));
    }

    // ---------- helpers ----------

    /// <summary>A PDF-like text: one page per string.</summary>
    private static ExtractedDocument Pages(params string[] pages) =>
        new([.. pages.Select((text, i) => new ExtractedPage(i + 1, text))]);

    /// <summary>
    /// Runs step 2, step 3 and the final save as the pipeline does, in one unit of work with the document's
    /// running job; returns the chunks made.
    /// </summary>
    private async Task<IReadOnlyList<TextChunk>> EmbedAsync(Guid documentId, ExtractedDocument text)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var pipeline = scope.Services.GetRequiredService<DocumentPipeline>();
        var document = await LoadAsync(scope, documentId);
        var job = await scope.Db.ProcessingJobs.SingleAsync(j => j.DocumentId == documentId, Ct);
        var chunks = await pipeline.ChunkAsync(document, text, Ct);
        var embedded = await pipeline.EmbedAsync(document, chunks, Ct);
        await pipeline.CompleteAsync(job, document, text, embedded, Ct);
        return chunks;
    }

    private static Task<Document> LoadAsync(DbScope scope, Guid documentId) =>
        scope.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]).SingleAsync(d => d.Id == documentId, Ct);

    private static Task<DocumentStatus> StatusAsync(DbScope scope, Guid documentId) =>
        scope.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(d => d.Id == documentId).Select(d => d.Status).SingleAsync(Ct);

    /// <summary>A PDF document that has just been extracted (so chunking may start), with its running job.</summary>
    private async Task<Document> SeedChunkingDocumentAsync()
    {
        var tenant = Tenant.Create("Embed company");
        tenant.ChangeSlug($"embed-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");
        var document = Document.FromUpload(
            tenant.Id, bot.Id, "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenant.Id}/{bot.Id}/{Guid.NewGuid():N}/original.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        document.StartExtracting();
        var job = ProcessingJob.For(document.Id);
        job.Start(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15));

        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.AddRange(tenant, bot, document, job);
        await scope.Db.SaveChangesAsync(Ct);
        return document;
    }

    /// <summary>A broken provider: returns one vector too few.</summary>
    private sealed class OneVectorShort : IEmbeddingService
    {
        public string Model => "broken";

        public int Dimensions => Chunk.EmbeddingDimensions;

        public decimal PricePerMillionTokensUsd => 0.06m;

        public Task<DocumentEmbeddings> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            Task.FromResult(new DocumentEmbeddings([.. texts.Skip(1).Select(FakeEmbeddingService.VectorFor)], 10));

        public Task<QueryEmbedding> EmbedQueryAsync(string text, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
