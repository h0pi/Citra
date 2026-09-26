using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Storage;
using Citra.Application.Features.Bots.Delete;
using Citra.Domain.Bots;
using Citra.Domain.Conversations;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Features.Bots;

[Collection(DatabaseCollection.Name)]
public sealed class DeleteBotTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deletes_the_bot_with_all_its_data_and_files()
    {
        var tenant = await SeedTenantAsync();
        var doomed = await SeedBotWithContentAsync(tenant, "Doomed");
        var kept = await SeedBotWithContentAsync(tenant, "Kept");

        await using (var scope = fixture.OpenScope(tenant.Id))
        {
            await scope.Send(new DeleteBotCommand(doomed.Bot.Id));
        }

        await using var system = fixture.OpenScope(tenantId: null);
        var db = system.Db;
        (await db.Bots.IgnoreQueryFilters().AnyAsync(b => b.Id == doomed.Bot.Id, Ct)).ShouldBeFalse();
        (await db.Documents.IgnoreQueryFilters().AnyAsync(d => d.BotId == doomed.Bot.Id, Ct)).ShouldBeFalse();
        (await db.Chunks.AnyAsync(c => c.BotId == doomed.Bot.Id, Ct)).ShouldBeFalse();
        (await db.ProcessingJobs.AnyAsync(j => j.DocumentId == doomed.Document.Id, Ct)).ShouldBeFalse();
        (await db.Conversations.IgnoreQueryFilters().AnyAsync(c => c.BotId == doomed.Bot.Id, Ct)).ShouldBeFalse();
        (await db.Messages.AnyAsync(m => m.ConversationId == doomed.Conversation.Id, Ct)).ShouldBeFalse();
        Directory.Exists(doomed.FileFolder).ShouldBeFalse("the bot's uploaded files must be deleted too");

        // The other bot of the same company is untouched.
        (await db.Bots.IgnoreQueryFilters().AnyAsync(b => b.Id == kept.Bot.Id, Ct)).ShouldBeTrue();
        (await db.Chunks.AnyAsync(c => c.BotId == kept.Bot.Id, Ct)).ShouldBeTrue();
        (await db.Messages.AnyAsync(m => m.ConversationId == kept.Conversation.Id, Ct)).ShouldBeTrue();
        Directory.Exists(kept.FileFolder).ShouldBeTrue();
    }

    [Fact]
    public async Task Bot_without_any_files_can_be_deleted()
    {
        var tenant = await SeedTenantAsync();
        var bot = Bot.Create(tenant.Id, "Empty");
        await SaveAsSystemAsync(bot);

        await using var scope = fixture.OpenScope(tenant.Id);
        await scope.Send(new DeleteBotCommand(bot.Id));

        (await scope.Db.Bots.AnyAsync(b => b.Id == bot.Id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Another_tenants_bot_is_not_found_and_survives()
    {
        var mine = await SeedTenantAsync();
        var theirs = await SeedTenantAsync();
        var foreign = await SeedBotWithContentAsync(theirs, "Theirs");

        await using (var scope = fixture.OpenScope(mine.Id))
        {
            await Should.ThrowAsync<NotFoundException>(() => scope.Send(new DeleteBotCommand(foreign.Bot.Id)));
        }

        await using var system = fixture.OpenScope(tenantId: null);
        (await system.Db.Bots.IgnoreQueryFilters().AnyAsync(b => b.Id == foreign.Bot.Id, Ct)).ShouldBeTrue();
        Directory.Exists(foreign.FileFolder).ShouldBeTrue();
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Read_only_roles_cannot_delete(UserRole role)
    {
        var tenant = await SeedTenantAsync();
        var bot = await SeedBotWithContentAsync(tenant, "Protected");

        await using var scope = fixture.OpenScope(tenant.Id, role);
        await Should.ThrowAsync<ForbiddenException>(() => scope.Send(new DeleteBotCommand(bot.Bot.Id)));

        (await scope.Db.Bots.AnyAsync(b => b.Id == bot.Bot.Id, Ct)).ShouldBeTrue();
        Directory.Exists(bot.FileFolder).ShouldBeTrue();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../other-folder")]
    [InlineData("a/../../outside")]
    public async Task Storage_refuses_paths_outside_its_folder(string path)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        var storage = scope.Services.GetRequiredService<IFileStorage>();

        await Should.ThrowAsync<InvalidOperationException>(() => storage.DeleteFolderAsync(path, Ct));
    }

    // ---------- helpers ----------

    private sealed record BotContent(Bot Bot, Document Document, Conversation Conversation, string FileFolder);

    private async Task<Tenant> SeedTenantAsync()
    {
        var tenant = Tenant.Create("Delete test company");
        tenant.ChangeSlug($"delete-{Guid.NewGuid():N}");
        await SaveAsSystemAsync(tenant);
        return tenant;
    }

    /// <summary>A bot with a document (plus its file on disk), a chunk, a processing job and a conversation.</summary>
    private async Task<BotContent> SeedBotWithContentAsync(Tenant tenant, string name)
    {
        var bot = Bot.Create(tenant.Id, name);
        var botFolder = StoragePaths.BotFolder(tenant.Id, bot.Id);
        var document = Document.FromUpload(
            tenant.Id, bot.Id, "guide.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{botFolder}/guide.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        var chunk = Chunk.Create(document.Id, bot.Id, 0, "Some text", null, 1, 0, 9, 3);
        var job = ProcessingJob.For(document.Id);
        var conversation = Conversation.StartInWidget(tenant.Id, bot.Id, "visitor-1");
        conversation.AddUserMessage("Hello?");

        await SaveAsSystemAsync(bot, document, chunk, job, conversation);

        var fileFolder = Path.Combine(fixture.StorageRoot, botFolder);
        Directory.CreateDirectory(fileFolder);
        await File.WriteAllTextAsync(Path.Combine(fileFolder, "guide.pdf"), "%PDF-fake", Ct);

        return new BotContent(bot, document, conversation, fileFolder);
    }

    private async Task SaveAsSystemAsync(params object[] entities)
    {
        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(entities);
        await system.Db.SaveChangesAsync(Ct);
    }
}
