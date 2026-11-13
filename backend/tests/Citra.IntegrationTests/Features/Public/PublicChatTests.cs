using System.Net;
using System.Net.Http.Json;
using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Features.Chat.SendMessage;
using Citra.Domain.Conversations;
using Citra.IntegrationTests.Features.Chat;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Public;

/// <summary>Website visitors chat with a public bot without a login, known only by a visitor id.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PublicChatTests(DatabaseFixture fixture)
{
    private const string VacationChunk = "Employees get 25 vacation days per year.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_visitor_asks_a_public_bot_and_the_chat_is_saved_as_theirs()
    {
        var seed = await PublicSeedAsync();

        await using var scope = fixture.OpenScope(tenantId: null);
        var events = await scope.Stream(Ask(seed, "visitor-1", null, "How many vacation days?"));

        var started = (MessageStarted)events[0];
        events.OfType<AnswerCompleted>().ShouldHaveSingleItem().CitedSources.ShouldBe([1]);
        await using var check = fixture.OpenScope(seed.Tenant.Id);
        var conversation = await check.Db.Conversations.Include(c => c.Messages).SingleAsync(c => c.Id == started.ConversationId, Ct);
        conversation.Channel.ShouldBe(ConversationChannel.Widget);
        conversation.VisitorId.ShouldBe("visitor-1");
        conversation.UserId.ShouldBeNull();
        conversation.TenantId.ShouldBe(seed.Tenant.Id);
        conversation.Messages.Count.ShouldBe(2);
        (await check.Db.EmbeddingUsages.CountAsync(u => u.BotId == seed.Bot.Id, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Only_the_same_visitor_can_continue_a_chat()
    {
        var seed = await PublicSeedAsync();
        await using var scope = fixture.OpenScope(tenantId: null);
        var first = (MessageStarted)(await scope.Stream(Ask(seed, "visitor-1", null, "How many vacation days?")))[0];

        var again = (MessageStarted)(await scope.Stream(Ask(seed, "visitor-1", first.ConversationId, "And next year?")))[0];
        again.ConversationId.ShouldBe(first.ConversationId);

        await Should.ThrowAsync<NotFoundException>(
            () => scope.Stream(Ask(seed, "visitor-2", first.ConversationId, "Can I read it?")));
        var adminChat = await AskAsync(fixture, seed, "An admin chat");
        await Should.ThrowAsync<NotFoundException>(() => scope.Stream(Ask(seed, "visitor-1", adminChat, "Mine?")));
    }

    [Fact]
    public async Task A_private_bot_or_a_missing_visitor_id_is_refused()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]); // not public
        await using var scope = fixture.OpenScope(tenantId: null);

        await Should.ThrowAsync<NotFoundException>(() => scope.Stream(Ask(seed, "visitor-1", null, "Hello?")));
        var publicSeed = await PublicSeedAsync();
        var error = await Should.ThrowAsync<ValidationException>(
            () => scope.Stream(Ask(publicSeed, " ", null, "Hello?")));
        error.Errors.Keys.ShouldBe(["VisitorId"]);
    }

    [Fact]
    public async Task A_visitor_who_asks_too_quickly_must_wait_a_minute()
    {
        var seed = await PublicSeedAsync();
        var limit = SendPublicMessageCommand.QuestionsPerVisitorPerMinute;
        var chat = await VisitorQuestionsAsync(seed, "visitor-1", limit);
        await using var scope = fixture.OpenScope(tenantId: null);

        await Should.ThrowAsync<TooManyRequestsException>(() => scope.Stream(Ask(seed, "visitor-1", null, "One more?")));
        (await scope.Stream(Ask(seed, "visitor-2", null, "My first?")))[0].ShouldBeOfType<MessageStarted>();

        // A minute later the earlier questions no longer count.
        await scope.Db.Messages.Where(m => m.ConversationId == chat)
            .ExecuteUpdateAsync(m => m.SetProperty(x => x.CreatedAt, x => x.CreatedAt.AddMinutes(-2)), Ct);
        (await scope.Stream(Ask(seed, "visitor-1", null, "Now?")))[0].ShouldBeOfType<MessageStarted>();
    }

    [Fact]
    public async Task A_bot_answers_website_visitors_only_up_to_its_daily_limit()
    {
        var seed = await PublicSeedAsync(dailyMessageLimit: 3);
        var earlier = await VisitorQuestionsAsync(seed, "visitor-1", 2);
        await AskAsync(fixture, seed, "Admin chats do not count");
        await using var scope = fixture.OpenScope(tenantId: null);

        (await scope.Stream(Ask(seed, "visitor-2", null, "The third?")))[0].ShouldBeOfType<MessageStarted>();
        var refused = await Should.ThrowAsync<TooManyRequestsException>(
            () => scope.Stream(Ask(seed, "visitor-3", null, "Still open?")));
        refused.Message.ShouldContain("today");

        // Yesterday's questions do not count.
        await scope.Db.Messages.Where(m => m.ConversationId == earlier)
            .ExecuteUpdateAsync(m => m.SetProperty(x => x.CreatedAt, x => x.CreatedAt.AddDays(-1)), Ct);
        (await scope.Stream(Ask(seed, "visitor-3", null, "Now?")))[0].ShouldBeOfType<MessageStarted>();
    }

    [Fact]
    public async Task A_bot_without_a_daily_limit_keeps_answering()
    {
        var seed = await PublicSeedAsync(dailyMessageLimit: null);
        await VisitorQuestionsAsync(seed, "visitor-1", 600);
        await using var scope = fixture.OpenScope(tenantId: null);

        (await scope.Stream(Ask(seed, "visitor-2", null, "Hello?")))[0].ShouldBeOfType<MessageStarted>();
    }

    [Fact]
    public async Task Other_websites_cannot_ask()
    {
        var seed = await PublicSeedAsync(allowedDomains: ["acme.com"]);
        await using var scope = fixture.OpenScope(tenantId: null);

        await Should.ThrowAsync<ForbiddenException>(
            () => scope.Stream(Ask(seed, "visitor-1", null, "Hello?") with { Origin = "https://evil.test" }));
        var events = await scope.Stream(Ask(seed, "visitor-1", null, "How many vacation days?") with { Origin = "https://acme.com" });
        events[0].ShouldBeOfType<MessageStarted>();
    }

    [Fact]
    public async Task The_endpoint_needs_no_login_and_streams_the_answer()
    {
        var seed = await PublicSeedAsync();
        await using var api = new ApiFactory(fixture);
        var client = api.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/public/bots/{seed.Tenant.Slug}/{seed.Bot.Slug}/chat",
            new { visitorId = "visitor-1", question = "How many vacation days?" },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("event: start");
        body.ShouldContain("event: done");
    }

    /// <summary>A widget chat of the visitor with this many questions, all asked just now.</summary>
    private async Task<Guid> VisitorQuestionsAsync(ChatSeed seed, string visitorId, int questions)
    {
        await using var scope = fixture.OpenScope(seed.Tenant.Id);
        var conversation = Conversation.StartInWidget(seed.Tenant.Id, seed.Bot.Id, visitorId);
        for (var i = 0; i < questions; i++)
        {
            conversation.AddUserMessage($"Question {i}?");
        }

        scope.Db.Conversations.Add(conversation);
        await scope.Db.SaveChangesAsync(Ct);
        return conversation.Id;
    }

    private static SendPublicMessageCommand Ask(ChatSeed seed, string visitorId, Guid? conversationId, string question) =>
        new(seed.Tenant.Slug, seed.Bot.Slug, visitorId, conversationId, question);

    private async Task<ChatSeed> PublicSeedAsync(string[]? allowedDomains = null, int? dailyMessageLimit = 500)
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        await using var scope = fixture.OpenScope(tenantId: null);
        var bot = await scope.Db.Bots.IgnoreQueryFilters([QueryFilters.Tenant]).SingleAsync(b => b.Id == seed.Bot.Id, Ct);
        bot.ConfigureWidget(isPublic: true, bot.WidgetColor, allowedDomains ?? [], [], dailyMessageLimit);
        await scope.Db.SaveChangesAsync(Ct);
        return seed;
    }
}
