using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Analytics;
using Citra.Domain.Conversations;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Citra.IntegrationTests.Features.Analytics.AnalyticsData;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Analytics;

/// <summary>C-232: questions the documents could not answer ("knowledge gaps"), newest first, with their chats.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetUnansweredQuestionsTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_unanswered_questions_newest_first_with_their_chats()
    {
        var seed = await SeedAsync(fixture, []);
        var adminChat = await ChatAsync(fixture, seed, seed.Bot.Id, question: "Where is the office?");
        await AskAgainAsync(seed.Tenant.Id, adminChat, "What is the parking fee?", answered: false);
        var widgetChat = await ChatAsync(fixture, seed, seed.Bot.Id, question: "Kako do kampusa?", answered: false, widget: true);
        await MoveBackAsync(fixture, widgetChat, days: 1);
        await MoveBackAsync(fixture, await ChatAsync(fixture, seed, seed.Bot.Id, question: "Old gap?", answered: false), days: 10);
        var otherBot = await AddBotAsync(fixture, seed.Tenant.Id);
        var otherChat = await ChatAsync(fixture, seed, otherBot.Id, question: "Other bot gap?", answered: false);
        var stranger = await SeedAsync(fixture, []);
        await ChatAsync(fixture, stranger, stranger.Bot.Id, question: "Stranger gap?", answered: false);

        await using var scope = Scope(fixture, seed);
        var gaps = await scope.Send(new GetUnansweredQuestionsQuery(seed.Bot.Id, Today.AddDays(-6), Today));
        var newest = await scope.Send(new GetUnansweredQuestionsQuery(seed.Bot.Id, Today.AddDays(-6), Today, Limit: 1));
        var all = await scope.Send(new GetUnansweredQuestionsQuery(null, Today, Today));

        gaps.Select(g => (g.ConversationId, g.BotId, g.Question, g.Channel)).ShouldBe([
            (adminChat, seed.Bot.Id, "What is the parking fee?", ConversationChannel.Admin),
            (widgetChat, seed.Bot.Id, "Kako do kampusa?", ConversationChannel.Widget),
        ]);
        gaps[0].AskedAt.ShouldBeGreaterThan(gaps[1].AskedAt);
        newest.Select(g => g.Question).ShouldBe(["What is the parking fee?"]);
        all.Select(g => g.ConversationId).ShouldBe([otherChat, adminChat], ignoreOrder: true);
    }

    [Fact]
    public async Task The_limit_and_the_period_are_checked()
    {
        var seed = await SeedAsync(fixture, []);

        await using var scope = Scope(fixture, seed);
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetUnansweredQuestionsQuery(null, Today, Today, Limit: 0)));
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetUnansweredQuestionsQuery(null, Today, Today, Limit: 201)));
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetUnansweredQuestionsQuery(null, Today, Today.AddDays(-1))));
    }

    /// <summary>A later question and answer in an existing chat (saved after the first ones).</summary>
    private async Task AskAgainAsync(Guid tenantId, Guid conversationId, string question, bool answered)
    {
        await using var scope = fixture.OpenScope(tenantId);
        var conversation = await scope.Db.Conversations.Include(c => c.Messages).SingleAsync(c => c.Id == conversationId, Ct);
        conversation.AddUserMessage(question);
        conversation.AddAssistantMessage("...").CompleteAnswer("Not found.", answered);
        await scope.Db.SaveChangesAsync(Ct);
    }
}
