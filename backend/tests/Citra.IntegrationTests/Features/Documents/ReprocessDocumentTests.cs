using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Documents.Reprocess;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Features.Documents;

[Collection(DatabaseCollection.Name)]
public sealed class ReprocessDocumentTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_failed_document_is_queued_again_with_its_error_cleared()
    {
        var (tenant, document) = await SeedAsync(Failed);

        await using var scope = fixture.OpenScope(tenant.Id);
        var dto = await scope.Send(new ReprocessDocumentCommand(document.Id));

        dto.Status.ShouldBe(DocumentStatus.Uploaded);
        dto.ErrorMessage.ShouldBeNull();
        dto.ProcessedAt.ShouldBeNull();

        await using var check = fixture.OpenScope(tenant.Id);
        var jobs = await check.Db.ProcessingJobs.Where(j => j.DocumentId == document.Id).ToListAsync(Ct);
        jobs.Count(j => j.Status == ProcessingJobStatus.Pending).ShouldBe(1);
    }

    [Fact]
    public async Task A_ready_document_loses_its_old_chunks_and_counts()
    {
        var (tenant, document) = await SeedAsync(Ready, chunks: 3);

        await using var scope = fixture.OpenScope(tenant.Id);
        var dto = await scope.Send(new ReprocessDocumentCommand(document.Id));

        dto.Status.ShouldBe(DocumentStatus.Uploaded);
        dto.PageCount.ShouldBe(0);
        dto.ChunkCount.ShouldBe(0);
        (await scope.Db.Chunks.AnyAsync(c => c.DocumentId == document.Id, Ct))
            .ShouldBeFalse("the Worker creates fresh chunks; old ones must not linger in answers");
    }

    [Fact]
    public async Task Only_this_documents_chunks_are_removed()
    {
        var (tenant, document) = await SeedAsync(Ready, chunks: 2);
        var (_, other) = await SeedAsync(Ready, chunks: 2, tenant: tenant);

        await using var scope = fixture.OpenScope(tenant.Id);
        await scope.Send(new ReprocessDocumentCommand(document.Id));

        (await scope.Db.Chunks.CountAsync(c => c.DocumentId == other.Id, Ct)).ShouldBe(2);
    }

    [Theory]
    [InlineData(DocumentStatus.Uploaded)]
    [InlineData(DocumentStatus.Extracting)]
    [InlineData(DocumentStatus.Embedding)]
    public async Task Refuses_while_the_document_is_queued_or_processing(DocumentStatus status)
    {
        var (tenant, document) = await SeedAsync(d => MoveTo(d, status), chunks: 1);

        await using var scope = fixture.OpenScope(tenant.Id);
        await Should.ThrowAsync<ConflictException>(() => scope.Send(new ReprocessDocumentCommand(document.Id)));

        await using var check = fixture.OpenScope(tenant.Id);
        (await check.Db.Documents.SingleAsync(d => d.Id == document.Id, Ct)).Status.ShouldBe(status);
        (await check.Db.ProcessingJobs.AnyAsync(j => j.DocumentId == document.Id, Ct)).ShouldBeFalse();
        (await check.Db.Chunks.AnyAsync(c => c.DocumentId == document.Id, Ct)).ShouldBeTrue("nothing may change");
    }

    [Fact]
    public async Task Another_companys_document_is_not_found()
    {
        var (_, foreign) = await SeedAsync(Failed);
        var (me, _) = await SeedAsync(Failed);

        await using var scope = fixture.OpenScope(me.Id);
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new ReprocessDocumentCommand(foreign.Id)));
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Read_only_roles_cannot_reprocess(UserRole role)
    {
        var (tenant, document) = await SeedAsync(Failed);

        await using var scope = fixture.OpenScope(tenant.Id, role);
        await Should.ThrowAsync<ForbiddenException>(() => scope.Send(new ReprocessDocumentCommand(document.Id)));
    }

    // ---------- helpers ----------

    private static void Failed(Document d)
    {
        d.StartExtracting();
        d.MarkFailed("Could not read the file.", DateTimeOffset.UtcNow);
    }

    private static void Ready(Document d)
    {
        d.StartExtracting();
        d.StartChunking();
        d.StartEmbedding();
        d.MarkReady(4, 3, DateTimeOffset.UtcNow);
    }

    private static void MoveTo(Document d, DocumentStatus status)
    {
        if (status >= DocumentStatus.Extracting) d.StartExtracting();
        if (status >= DocumentStatus.Chunking) d.StartChunking();
        if (status >= DocumentStatus.Embedding) d.StartEmbedding();
    }

    private async Task<(Tenant Tenant, Document Document)> SeedAsync(
        Action<Document> prepare, int chunks = 0, Tenant? tenant = null)
    {
        var entities = new List<object>();
        if (tenant is null)
        {
            tenant = Tenant.Create("Reprocess company");
            tenant.ChangeSlug($"reproc-{Guid.NewGuid():N}");
            entities.Add(tenant);
        }

        var bot = Bot.Create(tenant.Id, $"Support {Guid.NewGuid():N}");
        var document = Document.FromUpload(
            tenant.Id, bot.Id, "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenant.Id}/{bot.Id}/{Guid.NewGuid():N}/original.pdf",
            Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        prepare(document);
        entities.AddRange([bot, document]);
        entities.AddRange(Enumerable.Range(0, chunks)
            .Select(i => Chunk.Create(document.Id, bot.Id, i, $"Chunk {i}", null, 1, i * 10, i * 10 + 7, 2)));

        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(entities);
        await system.Db.SaveChangesAsync(Ct);
        return (tenant, document);
    }
}
