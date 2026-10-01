using Citra.Application.Common.Chat;
using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Chat.Conversations;
using Citra.Application.Features.Chat.SendMessage;
using Citra.Domain.Conversations;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Chat;

/// <summary>C-185/C-186: the chat list, reopening a chat, deleting one, and chat titles.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ConversationsTests(DatabaseFixture fixture)
{
    private const string VacationChunk = "Employees get 25 vacation days per year.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- the list ----------

    [Fact]
    public async Task Lists_the_users_chats_with_the_bot_most_recently_active_first()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var older = await AskAsync(fixture, seed, "First chat?");
        var newer = await AskAsync(fixture, seed, "Second chat?");
        await AskAsync(fixture, seed, "And again?", older); // the older chat becomes the most recent

        await using var scope = Scope(fixture, seed);
        var page = await scope.Send(new GetConversationsQuery(seed.Bot.Id));

        page.TotalCount.ShouldBe(2);
        page.Items.Select(c => (c.Id, c.Title, c.MessageCount)).ShouldBe([(older, "First chat?", 4), (newer, "Second chat?", 2)]);
        page.Items[0].BotId.ShouldBe(seed.Bot.Id);
        page.Items[0].UpdatedAt.ShouldBeGreaterThan(page.Items[0].CreatedAt);
    }

    [Fact]
    public async Task Leaves_out_other_users_chats_other_bots_and_website_visitors()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var mine = await AskAsync(fixture, seed, "Mine?");
        var colleague = seed with { UserId = await CreateUserAsync(fixture, seed.Tenant.Id, UserRole.Admin) };
        await AskAsync(fixture, colleague, "Colleague's?");
        var otherBot = seed with { Bot = await AddBotAsync(fixture, seed.Tenant.Id, [VacationChunk]) };
        await AskAsync(fixture, otherBot, "Other bot?");
        await WidgetConversationAsync(fixture, seed);

        await using var scope = Scope(fixture, seed);
        var page = await scope.Send(new GetConversationsQuery(seed.Bot.Id));

        page.Items.ShouldHaveSingleItem().Id.ShouldBe(mine);
    }

    [Fact]
    public async Task Pages_through_many_chats()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var ids = new List<Guid>();
        for (var i = 1; i <= 3; i++)
        {
            ids.Add(await AskAsync(fixture, seed, $"Chat {i}?"));
        }

        await using var scope = Scope(fixture, seed);
        var second = await scope.Send(new GetConversationsQuery(seed.Bot.Id, Page: 2, PageSize: 2));

        (second.TotalCount, second.TotalPages, second.Page, second.PageSize).ShouldBe((3, 2, 2, 2));
        second.Items.ShouldHaveSingleItem().Id.ShouldBe(ids[0]);
    }

    [Fact]
    public async Task A_bot_without_chats_has_an_empty_list()
    {
        var seed = await SeedAsync(fixture, []);

        await using var scope = Scope(fixture, seed);
        var page = await scope.Send(new GetConversationsQuery(seed.Bot.Id));

        (page.Items.Count, page.TotalCount).ShouldBe((0, 0));
    }

    [Fact]
    public async Task Another_companys_bot_or_an_unknown_one_is_not_found()
    {
        var foreign = await SeedAsync(fixture, []);
        var me = await SeedAsync(fixture, []);

        await using var scope = Scope(fixture, me);

        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetConversationsQuery(foreign.Bot.Id)));
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetConversationsQuery(Guid.NewGuid())));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Refuses_impossible_pages(int page, int pageSize)
    {
        var seed = await SeedAsync(fixture, []);

        await using var scope = Scope(fixture, seed);

        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetConversationsQuery(seed.Bot.Id, page, pageSize)));
    }

    // ---------- one chat ----------

    [Fact]
    public async Task Reopens_a_chat_with_its_messages_in_order_and_the_cited_sources()
    {
        var seed = await SeedAsync(fixture, [VacationChunk, "Parking is free."]);
        Guid conversationId;
        using (fixture.Chat.Script(_ => FakeChatModel.Stream(new ChatUsage("claude-test", 10, 5), "You get 25 days [1].")))
        {
            conversationId = await AskAsync(fixture, seed, "How many vacation days?");
        }

        await AskAsync(fixture, seed, "Where do I park?", conversationId);

        await using var scope = Scope(fixture, seed);
        var chat = await scope.Send(new GetConversationQuery(conversationId));

        (chat.Id, chat.BotId, chat.Title).ShouldBe((conversationId, seed.Bot.Id, "How many vacation days?"));
        chat.Messages.Select(m => (m.Role, m.Content)).ShouldBe([
            (MessageRole.User, "How many vacation days?"),
            (MessageRole.Assistant, "You get 25 days [1]."),
            (MessageRole.User, "Where do I park?"),
            (MessageRole.Assistant, chat.Messages[3].Content),
        ]);
        chat.Messages.Select(m => m.CreatedAt).ShouldBeInOrder(SortDirection.Ascending);

        var answer = chat.Messages[1];
        (answer.WasAnswered, answer.Feedback).ShouldBe((true, MessageFeedback.None));
        var citation = answer.Citations.ShouldHaveSingleItem();
        (citation.Number, citation.DocumentName, citation.PageNumber, citation.Snippet).ShouldBe((1, "Handbook.pdf", 1, VacationChunk));
        citation.ChunkId.ShouldNotBeNull();
        chat.Messages[0].Citations.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_question_and_answer_saved_at_the_same_moment_show_the_question_first()
    {
        var seed = await SeedAsync(fixture, []);
        var conversationId = await SavedTogetherAsync(fixture, seed);

        await using var scope = Scope(fixture, seed);
        var chat = await scope.Send(new GetConversationQuery(conversationId));

        chat.Messages[0].CreatedAt.ShouldBe(chat.Messages[1].CreatedAt);
        chat.Messages.Select(m => m.Content).ShouldBe(["Saved question?", "Saved answer."]);
    }

    [Fact]
    public async Task Citations_come_in_number_order()
    {
        var seed = await SeedAsync(fixture, ["One.", "Two.", "Three."]);
        Guid conversationId;
        using (fixture.Chat.Script(_ => FakeChatModel.Stream(new ChatUsage("claude-test", 10, 5), "C [3]. A [1].")))
        {
            conversationId = await AskAsync(fixture, seed, "Question?");
        }

        await using var scope = Scope(fixture, seed);
        var chat = await scope.Send(new GetConversationQuery(conversationId));

        chat.Messages[1].Citations.Select(c => c.Number).ShouldBe([1, 3]);
    }

    [Fact]
    public async Task Another_users_chat_or_an_unknown_one_is_not_found()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var conversationId = await AskAsync(fixture, seed, "Private?");
        var colleague = seed with { UserId = await CreateUserAsync(fixture, seed.Tenant.Id, UserRole.Owner), Role = UserRole.Owner };
        var outsider = await SeedAsync(fixture, []);

        await using (var scope = Scope(fixture, colleague))
        {
            await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetConversationQuery(conversationId)));
        }

        await using (var scope = Scope(fixture, outsider))
        {
            await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetConversationQuery(conversationId)));
        }

        await using (var scope = Scope(fixture, seed))
        {
            await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetConversationQuery(Guid.NewGuid())));
            var widget = await WidgetConversationAsync(fixture, seed);
            await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetConversationQuery(widget)));
        }
    }

    // ---------- deleting ----------

    [Fact]
    public async Task Deletes_a_chat_with_its_messages_and_sources()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var conversationId = await AskAsync(fixture, seed, "How many vacation days?");
        var kept = await AskAsync(fixture, seed, "Keep me?");

        await using (var scope = Scope(fixture, seed))
        {
            await scope.Send(new DeleteConversationCommand(conversationId));
            await Should.ThrowAsync<NotFoundException>(() => scope.Send(new DeleteConversationCommand(conversationId)));
        }

        await using var check = fixture.OpenScope(seed.Tenant.Id);
        (await check.Db.Conversations.AnyAsync(c => c.Id == conversationId, Ct)).ShouldBeFalse();
        (await check.Db.Messages.AnyAsync(m => m.ConversationId == conversationId, Ct)).ShouldBeFalse();
        (await check.Db.MessageCitations.AnyAsync(c => !check.Db.Messages.Any(m => m.Id == c.MessageId), Ct)).ShouldBeFalse();
        (await check.Db.Conversations.AnyAsync(c => c.Id == kept, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Another_users_chat_cannot_be_deleted()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var conversationId = await AskAsync(fixture, seed, "Private?");
        var colleague = seed with { UserId = await CreateUserAsync(fixture, seed.Tenant.Id, UserRole.Owner), Role = UserRole.Owner };

        await using (var scope = Scope(fixture, colleague))
        {
            await Should.ThrowAsync<NotFoundException>(() => scope.Send(new DeleteConversationCommand(conversationId)));
        }

        await using var check = fixture.OpenScope(seed.Tenant.Id);
        (await check.Db.Conversations.AnyAsync(c => c.Id == conversationId, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Viewers_may_delete_their_own_chats()
    {
        var seed = await SeedAsync(fixture, [VacationChunk], UserRole.Viewer);
        var conversationId = await AskAsync(fixture, seed, "Mine?");

        await using var scope = Scope(fixture, seed);
        await scope.Send(new DeleteConversationCommand(conversationId));

        (await scope.Db.Conversations.AnyAsync(c => c.Id == conversationId, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Nobody_logged_in_is_refused()
    {
        var seed = await SeedAsync(fixture, []);

        await using var scope = fixture.OpenScope(tenantId: null);

        await Should.ThrowAsync<UnauthorizedException>(() => scope.Send(new GetConversationsQuery(seed.Bot.Id)));
        await Should.ThrowAsync<UnauthorizedException>(() => scope.Send(new GetConversationQuery(Guid.NewGuid())));
        await Should.ThrowAsync<UnauthorizedException>(() => scope.Send(new DeleteConversationCommand(Guid.NewGuid())));
    }

    // ---------- C-186: titles ----------

    [Fact]
    public async Task A_chat_is_named_after_its_first_question_and_keeps_that_name()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var conversationId = await AskAsync(fixture, seed, "  How many\n vacation   days do I get?  ");
        await AskAsync(fixture, seed, "And what about next year?", conversationId);

        await using var scope = Scope(fixture, seed);
        (await scope.Send(new GetConversationQuery(conversationId))).Title.ShouldBe("How many vacation days do I get?");
    }

    [Theory]
    [InlineData("How many days?", "How many days?")]
    [InlineData("  Two\n\tlines  ", "Two lines")]
    [InlineData(
        "How many vacation days does a part-time employee get after working here for three years?",
        "How many vacation days does a part-time employee get after…")]
    [InlineData(
        "Antidisestablishmentarianismantidisestablishmentarianismantidisestablishmentarianism",
        "Antidisestablishmentarianismantidisestablishmentarianismanti…")]
    [InlineData(
        "Why antidisestablishmentarianismantidisestablishmentarianismantidisestablishment?",
        "Why antidisestablishmentarianismantidisestablishmentarianism…")]
    [InlineData(
        "What is the policy for vacation days, sick days, and remote work, with examples?",
        "What is the policy for vacation days, sick days, and remote…")]
    [InlineData(
        "Why are the vacation rules different for part-time staff; and what applies:",
        "Why are the vacation rules different for part-time staff…")]
    public void Titles_are_one_line_and_short(string question, string title)
    {
        ConversationTitle.FromQuestion(question).ShouldBe(title);
    }

    [Fact]
    public void A_question_of_exactly_the_maximum_length_is_kept_whole()
    {
        var question = new string('a', 30) + " " + new string('b', 29);
        question.Length.ShouldBe(ConversationTitle.MaxLength);

        ConversationTitle.FromQuestion(question).ShouldBe(question);
        ConversationTitle.FromQuestion(question + "b").ShouldEndWith("…");
    }
}
