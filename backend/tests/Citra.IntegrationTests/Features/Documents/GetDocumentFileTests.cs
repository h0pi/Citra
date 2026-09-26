using System.Text;
using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Storage;
using Citra.Application.Features.Documents.GetFile;
using Citra.Application.Features.Documents.Upload;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;

namespace Citra.IntegrationTests.Features.Documents;

[Collection(DatabaseCollection.Name)]
public sealed class GetDocumentFileTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\nsome pdf content\n%%EOF\n");

    [Fact]
    public async Task Streams_the_original_file_with_its_type_and_name()
    {
        var (tenant, documentId) = await UploadAsync("Employee Handbook.pdf");

        await using var scope = fixture.OpenScope(tenant.Id);
        var file = await scope.Send(new GetDocumentFileQuery(documentId));
        await using var content = file.Content;

        file.ContentType.ShouldBe("application/pdf");
        file.FileName.ShouldBe("Employee Handbook.pdf");
        (await ReadAllAsync(content)).ShouldBe(PdfBytes);
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Read_only_roles_can_open_files(UserRole role)
    {
        var (tenant, documentId) = await UploadAsync("a.pdf");

        await using var scope = fixture.OpenScope(tenant.Id, role);
        var file = await scope.Send(new GetDocumentFileQuery(documentId));
        await file.Content.DisposeAsync();
    }

    [Fact]
    public async Task Another_companys_file_is_not_found()
    {
        var (_, foreignDocument) = await UploadAsync("secret.pdf");
        var (me, _) = await UploadAsync("mine.pdf");

        await using var scope = fixture.OpenScope(me.Id);
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetDocumentFileQuery(foreignDocument)));
    }

    [Fact]
    public async Task A_web_page_has_no_file()
    {
        var (tenant, bot) = await SeedBotAsync();
        var page = Document.FromUrl(tenant.Id, bot.Id, new Uri("https://acme.com/help"));
        await using (var system = fixture.OpenScope(tenantId: null))
        {
            system.Db.Documents.Add(page);
            await system.Db.SaveChangesAsync(Ct);
        }

        await using var scope = fixture.OpenScope(tenant.Id);
        var error = await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetDocumentFileQuery(page.Id)));
        error.Message.ShouldContain("Web pages have no stored file");
    }

    [Fact]
    public async Task A_file_missing_from_disk_is_reported_as_not_found()
    {
        var (tenant, documentId) = await UploadAsync("lost.pdf");
        await using (var scope = fixture.OpenScope(tenant.Id))
        {
            var path = scope.Db.Documents.Single(d => d.Id == documentId).StoragePath!;
            File.Delete(Path.Combine(fixture.StorageRoot, path));
        }

        await using var again = fixture.OpenScope(tenant.Id);
        var error = await Should.ThrowAsync<NotFoundException>(() => again.Send(new GetDocumentFileQuery(documentId)));
        error.Message.ShouldContain("missing");
    }

    [Fact]
    public async Task Unknown_document_is_not_found()
    {
        var (tenant, _) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetDocumentFileQuery(Guid.NewGuid())));
    }

    // ---------- helpers ----------

    /// <summary>Uploads through the real command, so the file really is in storage.</summary>
    private async Task<(Tenant Tenant, Guid DocumentId)> UploadAsync(string fileName)
    {
        var (tenant, bot) = await SeedBotAsync();
        await using var scope = fixture.OpenScope(tenant.Id);
        var dto = await scope.Send(new UploadDocumentCommand(bot.Id, fileName, PdfBytes.Length, new MemoryStream(PdfBytes)));
        return (tenant, dto.Id);
    }

    private async Task<(Tenant Tenant, Bot Bot)> SeedBotAsync()
    {
        var tenant = Tenant.Create("File company");
        tenant.ChangeSlug($"file-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");

        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(tenant, bot);
        await system.Db.SaveChangesAsync(Ct);
        return (tenant, bot);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, Ct);
        return copy.ToArray();
    }
}
