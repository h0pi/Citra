using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Bots.GetById;
using Citra.Domain.Bots;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;

namespace Citra.IntegrationTests.Features.Bots;

[Collection(DatabaseCollection.Name)]
public sealed class GetBotByIdTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_all_settings_of_the_bot()
    {
        var tenant = await SeedTenantAsync();
        var bot = Bot.Create(tenant.Id, "Support", "Customer questions");
        bot.UpdateDetails("Support", "Customer questions", "Be brief.", "Welcome!");
        bot.ConfigureModel("claude-haiku-5-5");
        bot.ConfigureWidget(true, "#10B981", ["acme.com"], ["How do I reset my password?"], 100);
        await SaveAsSystemAsync(bot);

        await using var scope = fixture.OpenScope(tenant.Id);
        var dto = await scope.Send(new GetBotByIdQuery(bot.Id));

        dto.Id.ShouldBe(bot.Id);
        dto.Name.ShouldBe("Support");
        dto.Slug.ShouldBe("support");
        dto.TenantSlug.ShouldBe(tenant.Slug);
        dto.Description.ShouldBe("Customer questions");
        dto.SystemPrompt.ShouldBe("Be brief.");
        dto.WelcomeMessage.ShouldBe("Welcome!");
        dto.Model.ShouldBe("claude-haiku-5-5");
        dto.IsPublic.ShouldBeTrue();
        dto.WidgetColor.ShouldBe("#10B981");
        dto.AllowedDomains.ShouldBe(["acme.com"]);
        dto.StarterQuestions.ShouldBe(["How do I reset my password?"]);
        dto.DailyMessageLimit.ShouldBe(100);
        dto.CreatedAt.ShouldNotBe(default);
    }

    [Fact]
    public async Task Another_tenants_bot_is_not_found()
    {
        var mine = await SeedTenantAsync();
        var theirs = await SeedTenantAsync();
        var foreignBot = Bot.Create(theirs.Id, "Secret");
        await SaveAsSystemAsync(foreignBot);

        await using var scope = fixture.OpenScope(mine.Id);
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetBotByIdQuery(foreignBot.Id)));
    }

    [Fact]
    public async Task Unknown_id_is_not_found()
    {
        var tenant = await SeedTenantAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetBotByIdQuery(Guid.NewGuid())));
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Read_only_roles_can_view_settings(UserRole role)
    {
        var tenant = await SeedTenantAsync();
        var bot = Bot.Create(tenant.Id, "Visible");
        await SaveAsSystemAsync(bot);

        await using var scope = fixture.OpenScope(tenant.Id, role);
        (await scope.Send(new GetBotByIdQuery(bot.Id))).Name.ShouldBe("Visible");
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected()
    {
        await using var anonymous = fixture.OpenScope(tenantId: null);

        await Should.ThrowAsync<UnauthorizedException>(() => anonymous.Send(new GetBotByIdQuery(Guid.NewGuid())));
    }

    // ---------- helpers ----------

    private async Task<Tenant> SeedTenantAsync()
    {
        var tenant = Tenant.Create("Get test company");
        tenant.ChangeSlug($"get-{Guid.NewGuid():N}");
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
