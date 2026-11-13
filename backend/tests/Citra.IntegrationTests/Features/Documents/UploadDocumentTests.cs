using System.Security.Cryptography;
using System.Text;
using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Storage;
using Citra.Application.Features.Documents.Upload;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Features.Documents;

[Collection(DatabaseCollection.Name)]
public sealed class UploadDocumentTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\n%%EOF\n");
    private static readonly byte[] DocxBytes = [.. "PK\u0003\u0004"u8, .. Encoding.ASCII.GetBytes("word/document.xml ...")];
    private static readonly byte[] TextBytes = Encoding.UTF8.GetBytes("Returns are accepted within 30 days. Čćžšđ.");

    [Fact]
    public async Task Stores_the_file_and_queues_it_for_processing()
    {
        var (tenant, bot) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        var dto = await scope.Send(Upload(bot.Id, "Employee Handbook.PDF", PdfBytes));

        dto.FileName.ShouldBe("Employee Handbook.PDF");
        dto.BotId.ShouldBe(bot.Id);
        dto.SourceType.ShouldBe(DocumentSourceType.Pdf);
        dto.ContentType.ShouldBe("application/pdf");
        dto.SizeBytes.ShouldBe(PdfBytes.Length);
        dto.Status.ShouldBe(DocumentStatus.Uploaded);

        await using var check = fixture.OpenScope(tenant.Id);
        var document = await check.Db.Documents.SingleAsync(d => d.Id == dto.Id, Ct);
        document.StoragePath.ShouldBe(StoragePaths.OriginalFile(tenant.Id, bot.Id, dto.Id, "x.pdf"));
        document.ContentHash.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(PdfBytes)));
        File.ReadAllBytes(Path.Combine(fixture.StorageRoot, document.StoragePath!)).ShouldBe(PdfBytes);

        var job = await check.Db.ProcessingJobs.SingleAsync(j => j.DocumentId == dto.Id, Ct);
        job.Status.ShouldBe(ProcessingJobStatus.Pending);
    }

    [Theory]
    [InlineData("contract.docx", DocumentSourceType.Docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("faq.txt", DocumentSourceType.Text, "text/plain")]
    [InlineData("README.md", DocumentSourceType.Markdown, "text/markdown")]
    [InlineData("guide.markdown", DocumentSourceType.Markdown, "text/markdown")]
    public async Task Accepts_all_supported_types(string fileName, DocumentSourceType type, string contentType)
    {
        var (tenant, bot) = await SeedBotAsync();
        var bytes = type == DocumentSourceType.Docx ? DocxBytes : TextBytes;

        await using var scope = fixture.OpenScope(tenant.Id);
        var dto = await scope.Send(Upload(bot.Id, fileName, bytes));

        dto.SourceType.ShouldBe(type);
        dto.ContentType.ShouldBe(contentType);
    }

    [Theory]
    [InlineData("C:\\Users\\me\\Desktop\\Handbook.pdf", "Handbook.pdf")]
    [InlineData("../../../etc/Handbook.pdf", "Handbook.pdf")]
    [InlineData("  Handbook.pdf  ", "Handbook.pdf")]
    // Invisible characters are removed, so the name shown is the real one.
    [InlineData("photo‮gpj.pdf", "photogpj.pdf")] // right-to-left override: would show as "photofdp.jpg"
    [InlineData("Hand​book\u0007.pdf", "Handbook.pdf")]
    [InlineData("Q1 \t report\r\n.pdf", "Q1 report .pdf")]
    public async Task Keeps_only_the_file_name_from_paths(string sent, string stored)
    {
        var (tenant, bot) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        (await scope.Send(Upload(bot.Id, sent, PdfBytes))).FileName.ShouldBe(stored);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("photo.jpg")]
    [InlineData("no-extension")]
    [InlineData("old.doc")]
    public async Task Rejects_unsupported_file_types(string fileName)
    {
        var (tenant, bot) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        var error = await Should.ThrowAsync<ValidationException>(() => scope.Send(Upload(bot.Id, fileName, PdfBytes)));

        error.Errors["File"][0].ShouldContain("not supported");
        await AssertNothingStoredAsync(tenant, bot);
    }

    [Theory]
    [InlineData("fake.pdf", "just text, not a pdf")]
    [InlineData("fake.docx", "%PDF-1.4 a pdf renamed to docx")]
    [InlineData("binary.txt", "MZ\0\0\u0090 executable bytes")]
    public async Task Rejects_files_whose_content_does_not_match_the_extension(string fileName, string content)
    {
        var (tenant, bot) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        var error = await Should.ThrowAsync<ValidationException>(
            () => scope.Send(Upload(bot.Id, fileName, Encoding.Latin1.GetBytes(content))));

        error.Errors["File"][0].ShouldContain("does not look like a real");
        await AssertNothingStoredAsync(tenant, bot);
    }

    [Fact]
    public async Task Rejects_empty_files()
    {
        var (tenant, bot) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        var declaredEmpty = await Should.ThrowAsync<ValidationException>(() => scope.Send(Upload(bot.Id, "a.txt", [])));
        declaredEmpty.Errors["File"].ShouldContain("The file is empty.");

        // Announced as 10 bytes, but nothing arrives.
        var actuallyEmpty = await Should.ThrowAsync<ValidationException>(
            () => scope.Send(new UploadDocumentCommand(bot.Id, "a.txt", 10, new MemoryStream())));
        actuallyEmpty.Errors["File"].ShouldContain("The file is empty.");
    }

    [Fact]
    public async Task Rejects_files_over_the_limit_even_if_the_client_lies_about_the_size()
    {
        var (tenant, bot) = await SeedBotAsync();
        var tooBig = new byte[20 * 1024 * 1024 + 1];
        PdfBytes.CopyTo(tooBig, 0);

        await using var scope = fixture.OpenScope(tenant.Id);
        var honest = await Should.ThrowAsync<ValidationException>(() => scope.Send(Upload(bot.Id, "big.pdf", tooBig)));
        honest.Errors["File"][0].ShouldContain("20 MB");

        var lying = await Should.ThrowAsync<ValidationException>(
            () => scope.Send(new UploadDocumentCommand(bot.Id, "big.pdf", 1000, new MemoryStream(tooBig))));
        lying.Errors["File"][0].ShouldContain("20 MB");

        await AssertNothingStoredAsync(tenant, bot);
    }

    [Fact]
    public async Task Rejects_the_same_file_twice_in_one_bot_even_under_another_name()
    {
        var (tenant, bot) = await SeedBotAsync();
        await using (var first = fixture.OpenScope(tenant.Id))
        {
            await first.Send(Upload(bot.Id, "Handbook.pdf", PdfBytes));
        }

        await using var second = fixture.OpenScope(tenant.Id);
        var error = await Should.ThrowAsync<ValidationException>(
            () => second.Send(Upload(bot.Id, "Handbook (copy).pdf", PdfBytes)));

        error.Errors["File"].ShouldBe(["This file is already in this bot as \"Handbook.pdf\"."]);
        (await second.Db.Documents.CountAsync(d => d.BotId == bot.Id, Ct)).ShouldBe(1);
        FilesUnder(tenant, bot).Length.ShouldBe(1, "the rejected copy must not stay on disk");
    }

    [Fact]
    public async Task The_same_file_may_go_into_different_bots()
    {
        var (tenant, bot) = await SeedBotAsync();
        var otherBot = Bot.Create(tenant.Id, "HR");
        await using (var system = fixture.OpenScope(tenantId: null))
        {
            system.Db.Bots.Add(otherBot);
            await system.Db.SaveChangesAsync(Ct);
        }

        await using var scope = fixture.OpenScope(tenant.Id);
        await scope.Send(Upload(bot.Id, "Handbook.pdf", PdfBytes));
        await scope.Send(Upload(otherBot.Id, "Handbook.pdf", PdfBytes));

        (await scope.Db.Documents.CountAsync(d => d.ContentHash == HashOf(PdfBytes), Ct)).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task A_changed_file_with_the_same_name_is_not_a_duplicate()
    {
        var (tenant, bot) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        await scope.Send(Upload(bot.Id, "faq.txt", TextBytes));
        await scope.Send(Upload(bot.Id, "faq.txt", [.. TextBytes, .. " New answer."u8]));

        (await scope.Db.Documents.CountAsync(d => d.BotId == bot.Id, Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Two_simultaneous_uploads_of_the_same_file_store_it_once()
    {
        var (tenant, bot) = await SeedBotAsync();

        async Task<Exception?> UploadInOwnScope()
        {
            await using var scope = fixture.OpenScope(tenant.Id);
            try
            {
                await scope.Send(Upload(bot.Id, "Handbook.pdf", PdfBytes));
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => UploadInOwnScope()));

        results.Count(r => r is null).ShouldBe(1);
        results.Where(r => r is not null).ShouldAllBe(r => r is ValidationException);
        await using var check = fixture.OpenScope(tenant.Id);
        (await check.Db.Documents.CountAsync(d => d.BotId == bot.Id, Ct)).ShouldBe(1);
        FilesUnder(tenant, bot).Length.ShouldBe(1, "losers must remove their files");
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Read_only_roles_cannot_upload(UserRole role)
    {
        var (tenant, bot) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(tenant.Id, role);
        await Should.ThrowAsync<ForbiddenException>(() => scope.Send(Upload(bot.Id, "a.pdf", PdfBytes)));

        await AssertNothingStoredAsync(tenant, bot);
    }

    [Fact]
    public async Task Cannot_upload_into_another_companys_bot()
    {
        var (_, foreignBot) = await SeedBotAsync();
        var (me, _) = await SeedBotAsync();

        await using var scope = fixture.OpenScope(me.Id);
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(Upload(foreignBot.Id, "a.pdf", PdfBytes)));

        await using var system = fixture.OpenScope(tenantId: null);
        (await system.Db.Documents.IgnoreQueryFilters().AnyAsync(d => d.BotId == foreignBot.Id, Ct)).ShouldBeFalse();
    }

    // ---------- helpers ----------

    private static UploadDocumentCommand Upload(Guid botId, string fileName, byte[] bytes) =>
        new(botId, fileName, bytes.Length, new MemoryStream(bytes));

    private async Task<(Tenant Tenant, Bot Bot)> SeedBotAsync()
    {
        var tenant = Tenant.Create("Upload test company");
        tenant.ChangeSlug($"upload-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");

        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(tenant, bot);
        await system.Db.SaveChangesAsync(Ct);
        return (tenant, bot);
    }

    private static string HashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private string[] FilesUnder(Tenant tenant, Bot bot)
    {
        var botFolder = Path.Combine(fixture.StorageRoot, StoragePaths.BotFolder(tenant.Id, bot.Id));
        return Directory.Exists(botFolder) ? Directory.GetFiles(botFolder, "*", SearchOption.AllDirectories) : [];
    }

    /// <summary>No document row, no job and no file (not even a temp file) for this bot.</summary>
    private async Task AssertNothingStoredAsync(Tenant tenant, Bot bot)
    {
        await using var system = fixture.OpenScope(tenantId: null);
        (await system.Db.Documents.IgnoreQueryFilters().AnyAsync(d => d.BotId == bot.Id, Ct)).ShouldBeFalse();

        var botFolder = Path.Combine(fixture.StorageRoot, StoragePaths.BotFolder(tenant.Id, bot.Id));
        (Directory.Exists(botFolder) ? Directory.GetFiles(botFolder, "*", SearchOption.AllDirectories) : [])
            .ShouldBeEmpty();
    }
}
