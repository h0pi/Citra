using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Storage;
using Citra.Application.Features.Documents.Delete;
using Citra.Domain.Bots;
using Citra.Domain.Conversations;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Features.Documents;

[Collection(DatabaseCollection.Name)]
public sealed class DeleteDocumentTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deletes_the_record_chunks_job_and_file()
    {
        var seed = await SeedAsync();

        await using (var scope = fixture.OpenScope(seed.Tenant.Id))
        {
            await scope.Send(new DeleteDocumentCommand(seed.Doomed.Id));
        }

        await using var system = fixture.OpenScope(tenantId: null);
        var db = system.Db;
        (await db.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == seed.Doomed.Id, Ct)).ShouldBeFalse();
        (await db.Chunks.AnyAsync(c => c.DocumentId == seed.Doomed.Id, Ct)).ShouldBeFalse("answers must stop using it");
        (await db.ProcessingJobs.AnyAsync(j => j.DocumentId == seed.Doomed.Id, Ct)).ShouldBeFalse();
        Directory.Exists(DocumentFolderOnDisk(seed.Tenant, seed.Bot, seed.Doomed)).ShouldBeFalse();

        // The other document of the same bot is untouched.
        (await db.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == seed.Kept.Id, Ct)).ShouldBeTrue();
        (await db.Chunks.AnyAsync(c => c.DocumentId == seed.Kept.Id, Ct)).ShouldBeTrue();
        Directory.Exists(DocumentFolderOnDisk(seed.Tenant, seed.Bot, seed.Kept)).ShouldBeTrue();
    }

    [Fact]
    public async Task Past_answers_keep_their_citation_text()
    {
        var seed = await SeedAsync();

        await using (var scope = fixture.OpenScope(seed.Tenant.Id))
        {
            await scope.Send(new DeleteDocumentCommand(seed.Doomed.Id));
        }

        await using var system = fixture.OpenScope(tenantId: null);
        var citation = await system.Db.MessageCitations.SingleAsync(c => c.Id == seed.Citation.Id, Ct);
        citation.DocumentId.ShouldBeNull();
        citation.ChunkId.ShouldBeNull();
        citation.DocumentName.ShouldBe("Handbook.pdf");
        citation.Snippet.ShouldBe("Returns within 30 days.");
    }

    [Fact]
    public async Task A_web_page_without_stored_files_can_be_deleted()
    {
        var seed = await SeedAsync();
        var page = Document.FromUrl(seed.Tenant.Id, seed.Bot.Id, new Uri("https://acme.com/help"));
        await SaveAsSystemAsync(page);

        await using var scope = fixture.OpenScope(seed.Tenant.Id);
        await scope.Send(new DeleteDocumentCommand(page.Id));

        (await scope.Db.Documents.AnyAsync(d => d.Id == page.Id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Another_companys_document_is_not_found_and_survives()
    {
        var theirs = await SeedAsync();
        var mine = await SeedAsync();

        await using (var scope = fixture.OpenScope(mine.Tenant.Id))
        {
            await Should.ThrowAsync<NotFoundException>(() => scope.Send(new DeleteDocumentCommand(theirs.Doomed.Id)));
        }

        await using var system = fixture.OpenScope(tenantId: null);
        (await system.Db.Documents.IgnoreQueryFilters().AnyAsync(d => d.Id == theirs.Doomed.Id, Ct)).ShouldBeTrue();
        Directory.Exists(DocumentFolderOnDisk(theirs.Tenant, theirs.Bot, theirs.Doomed)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Read_only_roles_cannot_delete(UserRole role)
    {
        var seed = await SeedAsync();

        await using var scope = fixture.OpenScope(seed.Tenant.Id, role);
        await Should.ThrowAsync<ForbiddenException>(() => scope.Send(new DeleteDocumentCommand(seed.Doomed.Id)));

        (await scope.Db.Documents.AnyAsync(d => d.Id == seed.Doomed.Id, Ct)).ShouldBeTrue();
    }

    // ---------- helpers ----------

    private sealed record Seed(Tenant Tenant, Bot Bot, Document Doomed, Document Kept, MessageCitation Citation);

    /// <summary>A bot with two documents (each with a chunk, a job and a file) and an answer citing the first.</summary>
    private async Task<Seed> SeedAsync()
    {
        var tenant = Tenant.Create("Delete doc company");
        tenant.ChangeSlug($"deldoc-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");
        var doomed = NewDocument(tenant, bot, "Handbook.pdf");
        var kept = NewDocument(tenant, bot, "Pricing.pdf");
        var doomedChunk = Chunk.Create(doomed.Id, bot.Id, 0, "Returns within 30 days.", null, 1, 0, 23, 6);
        var keptChunk = Chunk.Create(kept.Id, bot.Id, 0, "Plans start at $10.", null, 1, 0, 19, 6);

        var chat = Conversation.StartInWidget(tenant.Id, bot.Id, "visitor");
        chat.AddUserMessage("Can I return it?");
        var answer = chat.AddAssistantMessage("Yes, within 30 days [1].");
        var citation = answer.AddCitation(1, doomedChunk.Id, doomed.Id, "Handbook.pdf", 1, "Returns within 30 days.", 0.91);

        await SaveAsSystemAsync(
            tenant, bot, doomed, kept, doomedChunk, keptChunk,
            ProcessingJob.For(doomed.Id), ProcessingJob.For(kept.Id), chat);

        foreach (var document in new[] { doomed, kept })
        {
            var folder = DocumentFolderOnDisk(tenant, bot, document);
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "original.pdf"), "%PDF-fake", Ct);
        }

        return new Seed(tenant, bot, doomed, kept, citation);
    }

    private static Document NewDocument(Tenant tenant, Bot bot, string name)
    {
        var id = Guid.CreateVersion7();
        return Document.FromUpload(
            tenant.Id, bot.Id, name, "application/pdf", 100, DocumentSourceType.Pdf,
            StoragePaths.OriginalFile(tenant.Id, bot.Id, id, name),
            Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), id);
    }

    private string DocumentFolderOnDisk(Tenant tenant, Bot bot, Document document) =>
        Path.Combine(fixture.StorageRoot, StoragePaths.DocumentFolder(tenant.Id, bot.Id, document.Id));

    private async Task SaveAsSystemAsync(params object[] entities)
    {
        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(entities);
        await system.Db.SaveChangesAsync(Ct);
    }
}
