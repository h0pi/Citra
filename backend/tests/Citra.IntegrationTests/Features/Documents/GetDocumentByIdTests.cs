using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Documents.GetById;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;

namespace Citra.IntegrationTests.Features.Documents;

[Collection(DatabaseCollection.Name)]
public sealed class GetDocumentByIdTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_a_processed_document_with_its_counts()
    {
        var processedAt = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var (tenant, document) = await SeedDocumentAsync(d =>
        {
            d.StartExtracting();
            d.StartChunking();
            d.StartEmbedding();
            d.MarkReady(pageCount: 12, chunkCount: 48, processedAt);
        });

        await using var scope = fixture.OpenScope(tenant.Id);
        var dto = await scope.Send(new GetDocumentByIdQuery(document.Id));

        dto.Id.ShouldBe(document.Id);
        dto.BotId.ShouldBe(document.BotId);
        dto.FileName.ShouldBe("Handbook.pdf");
        dto.Status.ShouldBe(DocumentStatus.Ready);
        dto.PageCount.ShouldBe(12);
        dto.ChunkCount.ShouldBe(48);
        dto.ProcessedAt.ShouldBe(processedAt);
        dto.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Explains_why_processing_failed()
    {
        var (tenant, document) = await SeedDocumentAsync(d =>
        {
            d.StartExtracting();
            d.MarkFailed("This PDF has no text layer (scanned PDF). OCR is not supported yet.", DateTimeOffset.UtcNow);
        });

        await using var scope = fixture.OpenScope(tenant.Id);
        var dto = await scope.Send(new GetDocumentByIdQuery(document.Id));

        dto.Status.ShouldBe(DocumentStatus.Failed);
        dto.ErrorMessage.ShouldBe("This PDF has no text layer (scanned PDF). OCR is not supported yet.");
    }

    [Fact]
    public async Task Another_companys_document_is_not_found()
    {
        var (_, foreign) = await SeedDocumentAsync();
        var (me, _) = await SeedDocumentAsync();

        await using var scope = fixture.OpenScope(me.Id);
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetDocumentByIdQuery(foreign.Id)));
    }

    [Fact]
    public async Task Unknown_id_is_not_found()
    {
        var (tenant, _) = await SeedDocumentAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetDocumentByIdQuery(Guid.NewGuid())));
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Read_only_roles_can_view_a_document(UserRole role)
    {
        var (tenant, document) = await SeedDocumentAsync();

        await using var scope = fixture.OpenScope(tenant.Id, role);
        (await scope.Send(new GetDocumentByIdQuery(document.Id))).Id.ShouldBe(document.Id);
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected()
    {
        await using var anonymous = fixture.OpenScope(tenantId: null);
        await Should.ThrowAsync<UnauthorizedException>(() => anonymous.Send(new GetDocumentByIdQuery(Guid.NewGuid())));
    }

    private async Task<(Tenant Tenant, Document Document)> SeedDocumentAsync(Action<Document>? prepare = null)
    {
        var tenant = Tenant.Create("Doc details company");
        tenant.ChangeSlug($"doc-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");
        var document = Document.FromUpload(
            tenant.Id, bot.Id, "Handbook.pdf", "application/pdf", 2048, DocumentSourceType.Pdf,
            $"{tenant.Id}/{bot.Id}/{Guid.NewGuid():N}/original.pdf",
            Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        prepare?.Invoke(document);

        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(tenant, bot, document);
        await system.Db.SaveChangesAsync(Ct);
        return (tenant, document);
    }
}
