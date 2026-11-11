using Citra.Application.Features.Chat.Conversations;
using Citra.Domain.Conversations;
using Citra.Domain.Tenants;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Features.Chat;

/// <summary>Review #18: chats nobody used for longer than the retention period are deleted with their messages.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ConversationCleanupTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Settings(int? ConversationDays) : IRetentionSettings;

    [Fact]
    public async Task Deletes_chats_unused_for_longer_than_the_retention_period_with_their_messages()
    {
        var tenant = Tenant.Create("Retention company");
        tenant.ChangeSlug($"retention-{Guid.NewGuid():N}");
        await using (var setup = fixture.OpenScope(tenantId: null))
        {
            setup.Db.Tenants.Add(tenant);
            await setup.Db.SaveChangesAsync(Ct);
        }

        var bot = await ChatData.AddBotAsync(fixture, tenant.Id);
        var old = await ChatAsync(Conversation.StartInWidget(tenant.Id, bot.Id, "visitor-1"), daysAgo: 31);
        var recent = await ChatAsync(Conversation.StartInWidget(tenant.Id, bot.Id, "visitor-2"), daysAgo: 29);

        var deleted = await CleanupAsync(30);

        deleted.ShouldBeGreaterThanOrEqualTo(1); // other tests' chats may be old too: only these two are checked
        await using var check = fixture.OpenScope(tenantId: null);
        (await check.Db.Conversations.IgnoreQueryFilters().Where(c => c.TenantId == tenant.Id).Select(c => c.Id).ToListAsync(Ct))
            .ShouldBe([recent]);
        (await check.Db.Messages.AnyAsync(m => m.ConversationId == old, Ct)).ShouldBeFalse("messages go with their chat");
    }

    [Fact]
    public async Task Without_a_retention_period_chats_are_kept_forever()
    {
        (await CleanupAsync(null)).ShouldBe(0);
    }

    private async Task<int> CleanupAsync(int? days)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        return await new ConversationCleanup(scope.Db, new Settings(days), TimeProvider.System).DeleteExpiredAsync(Ct);
    }

    private async Task<Guid> ChatAsync(Conversation conversation, int daysAgo)
    {
        conversation.AddUserMessage("Question?");
        await using var scope = fixture.OpenScope(conversation.TenantId);
        scope.Db.Conversations.Add(conversation);
        await scope.Db.SaveChangesAsync(Ct);
        await scope.Db.Conversations.Where(c => c.Id == conversation.Id)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.UpdatedAt, x => x.UpdatedAt.AddDays(-daysAgo)), Ct);
        return conversation.Id;
    }
}
