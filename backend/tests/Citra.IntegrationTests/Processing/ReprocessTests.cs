using System.Text;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Storage;
using Citra.Application.Features.Documents.Processing;
using Citra.Application.Features.Documents.Reprocess;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Usage;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Processing;

/// <summary>
/// A document's old chunks are replaced by the new ones, never mixed with them — when it is reprocessed
/// (the command removes them at once) and in the final step itself (whatever left chunks behind), without
/// touching any other document's chunks. Real command, real runner; isolated database (the runner takes the
/// oldest due job of all).
/// </summary>
[Collection(IsolatedDatabaseCollection.Name)]
public sealed class ReprocessTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Db.ProcessingJobs.ExecuteDeleteAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Leftover_chunks_are_replaced_and_other_documents_keep_theirs()
    {
        var (document, sibling) = await SeedTwoDocumentsAsync("Employees get 25 days of paid vacation.");

        // Whatever the reason, the document still has chunks — even at positions the new ones will take.
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            scope.Db.Chunks.AddRange(
                Chunk.Create(document.Id, document.BotId, 0, "stale 0", null, null, 0, 7, 2),
                Chunk.Create(document.Id, document.BotId, 5, "stale 5", null, null, 0, 7, 2),
                Chunk.Create(sibling.Id, sibling.BotId, 0, "sibling", null, null, 0, 7, 1));
            await scope.Db.SaveChangesAsync(Ct);
            scope.Db.ProcessingJobs.Add(ProcessingJob.For(document.Id));
            await scope.Db.SaveChangesAsync(Ct);
        }

        (await RunAsync()).ShouldBeTrue();

        var chunks = await ChunksAsync(document.Id);
        chunks.Select(c => c.Content).ShouldBe(["Employees get 25 days of paid vacation."], "only the new chunk, no stale ones");
        (await DocumentAsync(document.Id)).ChunkCount.ShouldBe(1);
        (await ChunksAsync(sibling.Id)).ShouldHaveSingleItem().Content.ShouldBe("sibling", "another document of the same bot");
    }

    [Fact]
    public async Task Reprocessing_a_changed_file_replaces_all_its_chunks()
    {
        var (document, _) = await SeedTwoDocumentsAsync("Old policy: employees get 20 days of paid vacation.");
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            scope.Db.ProcessingJobs.Add(ProcessingJob.For(document.Id));
            await scope.Db.SaveChangesAsync(Ct);
        }

        (await RunAsync()).ShouldBeTrue();
        var before = await ChunksAsync(document.Id);
        before.ShouldHaveSingleItem().Content.ShouldContain("20 days");

        // The file is corrected, and an editor of the company presses Reprocess.
        await StoreAsync(document.StoragePath!, "New policy: employees get 25 days of paid vacation.\n\nUp to 5 days carry over.");
        await using (var editor = fixture.OpenScope(document.TenantId, Citra.Domain.Users.UserRole.Admin))
        {
            (await editor.Send(new ReprocessDocumentCommand(document.Id))).Status.ShouldBe(DocumentStatus.Uploaded);
        }

        (await ChunksAsync(document.Id)).ShouldBeEmpty("removed at once, so the old text is never mixed with the new");

        (await RunAsync()).ShouldBeTrue();

        var after = await ChunksAsync(document.Id);
        string.Join("\n", after.Select(c => c.Content)).ShouldContain("25 days");
        after.ShouldAllBe(c => !c.Content.Contains("20 days"));
        after.Select(c => c.Id).ShouldNotContain(before[0].Id);
        after.Select(c => c.Index).ShouldBe(Enumerable.Range(0, after.Count));

        var reprocessed = await DocumentAsync(document.Id);
        reprocessed.Status.ShouldBe(DocumentStatus.Ready);
        reprocessed.ChunkCount.ShouldBe(after.Count);

        await using var check = fixture.OpenScope(tenantId: null);
        var jobs = await check.Db.ProcessingJobs.AsNoTracking().Where(j => j.DocumentId == document.Id).ToListAsync(Ct);
        jobs.Count.ShouldBe(2, "one per processing, kept as history");
        jobs.ShouldAllBe(j => j.Status == ProcessingJobStatus.Succeeded);
        var usages = await check.Db.EmbeddingUsages.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(u => u.DocumentId == document.Id).ToListAsync(Ct);
        usages.Count.ShouldBe(2, "both embeddings cost money");
        usages.ShouldAllBe(u => u.Purpose == EmbeddingPurpose.Document);
    }

    [Fact]
    public async Task A_failed_document_can_be_fixed_and_reprocessed()
    {
        var (document, _) = await SeedTwoDocumentsAsync("   \n\n   ");
        await using (var scope = fixture.OpenScope(tenantId: null))
        {
            scope.Db.ProcessingJobs.Add(ProcessingJob.For(document.Id));
            await scope.Db.SaveChangesAsync(Ct);
        }

        (await RunAsync()).ShouldBeTrue();
        (await DocumentAsync(document.Id)).Status.ShouldBe(DocumentStatus.Failed);

        await StoreAsync(document.StoragePath!, "Employees get 25 days of paid vacation.");
        await using (var editor = fixture.OpenScope(document.TenantId, Citra.Domain.Users.UserRole.Admin))
        {
            await editor.Send(new ReprocessDocumentCommand(document.Id));
        }

        (await RunAsync()).ShouldBeTrue();

        var fixedDocument = await DocumentAsync(document.Id);
        fixedDocument.Status.ShouldBe(DocumentStatus.Ready);
        fixedDocument.ErrorMessage.ShouldBeNull();
        fixedDocument.ChunkCount.ShouldBe(1);
        (await ChunksAsync(document.Id)).ShouldHaveSingleItem();
    }

    // ---------- helpers ----------

    private async Task<bool> RunAsync()
    {
        await using var worker = fixture.OpenScope(tenantId: null);
        return await worker.Services.GetRequiredService<IProcessingJobRunner>().RunNextAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    private async Task<List<Chunk>> ChunksAsync(Guid documentId)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        return await scope.Db.Chunks.AsNoTracking().Where(c => c.DocumentId == documentId).OrderBy(c => c.Index).ToListAsync(Ct);
    }

    private async Task<Document> DocumentAsync(Guid documentId)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        return await scope.Db.Documents.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking().SingleAsync(d => d.Id == documentId, Ct);
    }

    private async Task StoreAsync(string path, string content)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Services.GetRequiredService<IFileStorage>().SaveAsync(path, new MemoryStream(Encoding.UTF8.GetBytes(content)), Ct);
    }

    /// <summary>Two .txt documents of one bot, files really stored; the first has <paramref name="content"/>.</summary>
    private async Task<(Document Document, Document Sibling)> SeedTwoDocumentsAsync(string content)
    {
        var tenant = Tenant.Create("Reprocess company");
        tenant.ChangeSlug($"reprocess-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");

        Document NewDocument(string fileName)
        {
            var id = Guid.CreateVersion7();
            return Document.FromUpload(
                tenant.Id, bot.Id, fileName, "text/plain", 10, DocumentSourceType.Text,
                StoragePaths.OriginalFile(tenant.Id, bot.Id, id, fileName), Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), id);
        }

        var document = NewDocument("policy.txt");
        var sibling = NewDocument("other.txt");
        await StoreAsync(document.StoragePath!, content);
        await StoreAsync(sibling.StoragePath!, "Another document.");

        await using var scope = fixture.OpenScope(tenantId: null);
        scope.Db.AddRange(tenant, bot, document, sibling);
        await scope.Db.SaveChangesAsync(Ct);
        return (document, sibling);
    }
}
