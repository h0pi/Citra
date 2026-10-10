using Citra.Domain.Conversations;
using Citra.Domain.Usage;
using Citra.IntegrationTests.Features.Chat;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Features.Analytics;

/// <summary>Chats with recorded usage for the analytics tests, and moving them back in time.</summary>
internal static class AnalyticsData
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>A chat of the seed's company with the bot: <paramref name="question"/> asked <paramref name="questions"/> times, each answered.</summary>
    public static async Task<Guid> ChatAsync(
        DatabaseFixture fixture, ChatSeed seed, Guid botId, bool answered = true, int latencyMs = 1000, decimal cost = 0m,
        bool widget = false, int questions = 1, string question = "Question?", MessageFeedback feedback = MessageFeedback.None)
    {
        var conversation = widget
            ? Conversation.StartInWidget(seed.Tenant.Id, botId, "visitor-1")
            : Conversation.StartInAdmin(seed.Tenant.Id, botId, seed.UserId);
        for (var i = 0; i < questions; i++)
        {
            conversation.AddUserMessage(question);
            var answer = conversation.AddAssistantMessage("...");
            answer.CompleteAnswer("Answer.", answered);
            answer.RecordUsage(10, 20, cost, latencyMs);
            answer.GiveFeedback(feedback);
        }

        await using var scope = fixture.OpenScope(seed.Tenant.Id);
        scope.Db.Conversations.Add(conversation);
        await scope.Db.SaveChangesAsync(Ct);
        return conversation.Id;
    }

    public static async Task MoveBackAsync(DatabaseFixture fixture, Guid conversationId, int days)
    {
        await using var scope = fixture.OpenScope(tenantId: null);
        await scope.Db.Conversations.IgnoreQueryFilters().Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.CreatedAt, x => x.CreatedAt.AddDays(-days)), Ct);
        await scope.Db.Messages.Where(m => m.ConversationId == conversationId)
            .ExecuteUpdateAsync(m => m.SetProperty(x => x.CreatedAt, x => x.CreatedAt.AddDays(-days)), Ct);
    }

    /// <summary>Embedding cost of a question to the bot.</summary>
    public static async Task<Guid> UsageAsync(DatabaseFixture fixture, Guid tenantId, Guid botId, decimal cost)
    {
        await using var scope = fixture.OpenScope(tenantId);
        var usage = EmbeddingUsage.ForQuery(tenantId, botId, "voyage-4", 10, cost);
        scope.Db.EmbeddingUsages.Add(usage);
        await scope.Db.SaveChangesAsync(Ct);
        return usage.Id;
    }
}
