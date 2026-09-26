using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Bots.List;
using Citra.Domain.Bots;
using Citra.Domain.Conversations;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;

namespace Citra.IntegrationTests.Features.Bots;

[Collection(DatabaseCollection.Name)]
public sealed class GetBotsTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_the_tenants_bots_by_name_with_document_and_message_counts()
    {
        var tenant = await SeedTenantAsync();
        var support = Bot.Create(tenant.Id, "Support", "Customer questions");
        var hr = Bot.Create(tenant.Id, "HR");
        var empty = Bot.Create(tenant.Id, "Archive");

        // Support: 2 documents, 3 messages in 2 conversations. HR: 1 document, 1 message. Archive: nothing.
        var chat1 = Conversation.StartInWidget(tenant.Id, support.Id, "v1");
        chat1.AddUserMessage("Hi");
        chat1.AddAssistantMessage("Hello!");
        var chat2 = Conversation.StartInWidget(tenant.Id, support.Id, "v2");
        chat2.AddUserMessage("Price?");
        var chat3 = Conversation.StartInWidget(tenant.Id, hr.Id, "v3");
        chat3.AddUserMessage("Vacation days?");

        await SaveAsSystemAsync(
            support, hr, empty,
            NewDocument(tenant.Id, support.Id), NewDocument(tenant.Id, support.Id), NewDocument(tenant.Id, hr.Id),
            chat1, chat2, chat3);

        await using var scope = fixture.OpenScope(tenant.Id);
        var bots = await scope.Send(new GetBotsQuery());

        bots.Select(b => b.Name).ShouldBe(["Archive", "HR", "Support"]);

        var supportRow = bots.Single(b => b.Id == support.Id);
        supportRow.DocumentCount.ShouldBe(2);
        supportRow.MessageCount.ShouldBe(3);
        supportRow.Description.ShouldBe("Customer questions");
        supportRow.Slug.ShouldBe("support");
        supportRow.WidgetColor.ShouldBe(Bot.DefaultWidgetColor);
        supportRow.IsPublic.ShouldBeFalse();

        var hrRow = bots.Single(b => b.Id == hr.Id);
        hrRow.DocumentCount.ShouldBe(1);
        hrRow.MessageCount.ShouldBe(1);

        var emptyRow = bots.Single(b => b.Id == empty.Id);
        emptyRow.DocumentCount.ShouldBe(0);
        emptyRow.MessageCount.ShouldBe(0);
    }

    [Fact]
    public async Task Never_includes_other_tenants_bots()
    {
        var mine = await SeedTenantAsync();
        var theirs = await SeedTenantAsync();
        var myBot = Bot.Create(mine.Id, "Mine");
        var theirBot = Bot.Create(theirs.Id, "Theirs");
        await SaveAsSystemAsync(myBot, theirBot);

        await using var scope = fixture.OpenScope(mine.Id);
        var bots = await scope.Send(new GetBotsQuery());

        bots.Select(b => b.Id).ShouldBe([myBot.Id]);
    }

    [Fact]
    public async Task A_new_company_has_an_empty_list()
    {
        var tenant = await SeedTenantAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        (await scope.Send(new GetBotsQuery())).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Read_only_roles_can_list_bots(UserRole role)
    {
        var tenant = await SeedTenantAsync();
        await SaveAsSystemAsync(Bot.Create(tenant.Id, "Visible"));

        await using var scope = fixture.OpenScope(tenant.Id, role);
        (await scope.Send(new GetBotsQuery())).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected()
    {
        await using var anonymous = fixture.OpenScope(tenantId: null);

        await Should.ThrowAsync<UnauthorizedException>(() => anonymous.Send(new GetBotsQuery()));
    }

    // ---------- helpers ----------

    private static Document NewDocument(Guid tenantId, Guid botId) => Document.FromUpload(
        tenantId, botId, "guide.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
        $"{tenantId}/{botId}/{Guid.NewGuid():N}.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));

    private async Task<Tenant> SeedTenantAsync()
    {
        var tenant = Tenant.Create("List test company");
        tenant.ChangeSlug($"list-{Guid.NewGuid():N}");
        await SaveAsSystemAsync(tenant);
        return tenant;
    }

    private async Task SaveAsSystemAsync(params object[] entities)
    {
        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(entities);
        await system.Db.SaveChangesAsync(Ct);
    }
}
