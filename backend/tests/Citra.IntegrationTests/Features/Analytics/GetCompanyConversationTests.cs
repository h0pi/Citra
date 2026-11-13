using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Analytics;
using Citra.Domain.Conversations;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using static Citra.IntegrationTests.Features.Analytics.AnalyticsData;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Analytics;

/// <summary>Owners and admins read any chat of the company (e.g. from the knowledge gaps), never another company's.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetCompanyConversationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task An_admin_reads_a_colleagues_chat_and_a_website_chat()
    {
        var seed = await SeedAsync(fixture, []);
        var colleague = seed with { UserId = await CreateUserAsync(fixture, seed.Tenant.Id, UserRole.Viewer) };
        var colleaguesChat = await ChatAsync(fixture, colleague, seed.Bot.Id, question: "Where do I park?", answered: false);
        var websiteChat = await ChatAsync(fixture, seed, seed.Bot.Id, question: "Opening hours?", widget: true);

        await using var scope = Scope(fixture, seed);
        var chat = await scope.Send(new GetCompanyConversationQuery(colleaguesChat));
        var website = await scope.Send(new GetCompanyConversationQuery(websiteChat));

        chat.Messages.Select(m => (m.Role, m.Content, m.WasAnswered)).ShouldBe([
            (MessageRole.User, "Where do I park?", true),
            (MessageRole.Assistant, "Answer.", false),
        ]);
        website.Messages[0].Content.ShouldBe("Opening hours?");
    }

    [Fact]
    public async Task Viewers_cannot_and_another_companys_chat_is_not_found()
    {
        var seed = await SeedAsync(fixture, []);
        var chat = await ChatAsync(fixture, seed, seed.Bot.Id);
        var viewer = seed with { UserId = await CreateUserAsync(fixture, seed.Tenant.Id, UserRole.Viewer), Role = UserRole.Viewer };
        var stranger = await SeedAsync(fixture, []);

        await using (var asViewer = Scope(fixture, viewer))
        {
            await Should.ThrowAsync<ForbiddenException>(() => asViewer.Send(new GetCompanyConversationQuery(chat)));
        }

        await using var asStranger = Scope(fixture, stranger);
        await Should.ThrowAsync<NotFoundException>(() => asStranger.Send(new GetCompanyConversationQuery(chat)));
    }
}
