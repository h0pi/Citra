using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Analytics;
using Citra.Domain.Conversations;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using static Citra.IntegrationTests.Features.Analytics.AnalyticsData;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Analytics;

/// <summary>Owners and admins browse all of the company's chats, newest activity first, by bot and channel.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetCompanyConversationsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Lists_every_users_and_website_chats_newest_first_with_filters_and_pages()
    {
        var seed = await SeedAsync(fixture, []);
        var colleague = seed with { UserId = await CreateUserAsync(fixture, seed.Tenant.Id, UserRole.Viewer) };
        var colleagues = await ChatAsync(fixture, colleague, seed.Bot.Id, questions: 2);
        var website = await ChatAsync(fixture, seed, seed.Bot.Id, widget: true);
        var otherBot = await AddBotAsync(fixture, seed.Tenant.Id);
        var other = await ChatAsync(fixture, seed, otherBot.Id);
        var stranger = await SeedAsync(fixture, []);
        await ChatAsync(fixture, stranger, stranger.Bot.Id);

        await using var scope = Scope(fixture, seed);
        var all = await scope.Send(new GetCompanyConversationsQuery(null, null));
        var widgetOnly = await scope.Send(new GetCompanyConversationsQuery(null, ConversationChannel.Widget));
        var oneBotAdmin = await scope.Send(new GetCompanyConversationsQuery(seed.Bot.Id, ConversationChannel.Admin));
        var second = await scope.Send(new GetCompanyConversationsQuery(null, null, Page: 2, PageSize: 2));

        all.Items.Select(c => (c.Id, c.Channel)).ShouldBe([
            (other, ConversationChannel.Admin),
            (website, ConversationChannel.Widget),
            (colleagues, ConversationChannel.Admin),
        ]);
        all.TotalCount.ShouldBe(3);
        all.Items[2].MessageCount.ShouldBe(4);
        all.Items[2].BotId.ShouldBe(seed.Bot.Id);
        widgetOnly.Items.Select(c => c.Id).ShouldBe([website]);
        oneBotAdmin.Items.Select(c => c.Id).ShouldBe([colleagues]);
        second.Items.Select(c => c.Id).ShouldBe([colleagues]);
        second.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Only_owners_and_admins_and_valid_pages()
    {
        var seed = await SeedAsync(fixture, []);
        var viewer = seed with { UserId = await CreateUserAsync(fixture, seed.Tenant.Id, UserRole.Viewer), Role = UserRole.Viewer };

        await using (var asViewer = Scope(fixture, viewer))
        {
            await Should.ThrowAsync<ForbiddenException>(() => asViewer.Send(new GetCompanyConversationsQuery(null, null)));
        }

        await using var scope = Scope(fixture, seed);
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetCompanyConversationsQuery(null, null, Page: 0)));
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetCompanyConversationsQuery(null, null, PageSize: 101)));
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetCompanyConversationsQuery(null, (ConversationChannel)9)));
    }
}
