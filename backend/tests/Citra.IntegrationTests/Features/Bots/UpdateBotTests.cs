using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Bots;
using Citra.Application.Features.Bots.Create;
using Citra.Application.Features.Bots.Update;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Features.Bots;

[Collection(DatabaseCollection.Name)]
public sealed class UpdateBotTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Saves_all_settings_and_keeps_the_slug()
    {
        var (tenant, bot) = await SeedBotAsync("Support");

        await using var scope = fixture.OpenScope(tenant.Id);
        var updated = await scope.Send(ValidUpdate(bot) with
        {
            Name = "  Sales Assistant ",
            Description = "Helps with pricing",
            SystemPrompt = "Be brief.",
            WelcomeMessage = "Hello!",
            Model = "claude-haiku-5-5",
            WidgetColor = "#10b981",
            StarterQuestions = ["  How many vacation days?  ", " ", "Where do I park?"],
            IsPublic = true,
            AllowedDomains = [" Acme.com ", "", "shop.test"],
            DailyMessageLimit = 50,
        });

        updated.Name.ShouldBe("Sales Assistant");
        updated.Slug.ShouldBe("support", "renaming must not break widget URLs already embedded on websites");
        updated.TenantSlug.ShouldBe(tenant.Slug);
        updated.Description.ShouldBe("Helps with pricing");
        updated.SystemPrompt.ShouldBe("Be brief.");
        updated.WelcomeMessage.ShouldBe("Hello!");
        updated.Model.ShouldBe("claude-haiku-5-5");
        updated.WidgetColor.ShouldBe("#10B981");
        updated.StarterQuestions.ShouldBe(["How many vacation days?", "Where do I park?"]);
        updated.IsPublic.ShouldBeTrue();
        updated.AllowedDomains.ShouldBe(["acme.com", "shop.test"]);
        updated.DailyMessageLimit.ShouldBe(50);
        updated.UpdatedAt.ShouldBeGreaterThanOrEqualTo(bot.UpdatedAt);

        await using var check = fixture.OpenScope(tenant.Id);
        var saved = await check.Db.Bots.SingleAsync(b => b.Id == bot.Id, Ct);
        saved.Name.ShouldBe("Sales Assistant");
        saved.Model.ShouldBe("claude-haiku-5-5");
    }

    [Fact]
    public async Task Empty_optional_fields_are_cleared()
    {
        var (tenant, bot) = await SeedBotAsync("Support");

        await using (var scope = fixture.OpenScope(tenant.Id))
        {
            await scope.Send(ValidUpdate(bot) with { SystemPrompt = "Be brief.", Model = "claude-sonnet-5-5" });
        }

        await using var again = fixture.OpenScope(tenant.Id);
        var cleared = await again.Send(ValidUpdate(bot) with { Description = " ", SystemPrompt = "", Model = null });

        cleared.Description.ShouldBeNull();
        cleared.SystemPrompt.ShouldBeNull();
        cleared.Model.ShouldBeNull("no model = use the app default");
    }

    [Fact]
    public async Task Another_tenants_bot_is_not_found_and_not_changed()
    {
        var (_, foreignBot) = await SeedBotAsync("Their bot");
        var (me, _) = await SeedBotAsync("My bot");

        await using var scope = fixture.OpenScope(me.Id);
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(ValidUpdate(foreignBot) with { Name = "Hacked" }));

        await using var system = fixture.OpenScope(tenantId: null);
        var untouched = await system.Db.Bots.IgnoreQueryFilters().SingleAsync(b => b.Id == foreignBot.Id, Ct);
        untouched.Name.ShouldBe("Their bot");
    }

    [Fact]
    public async Task Unknown_bot_is_not_found()
    {
        var (tenant, _) = await SeedBotAsync("Support");

        await using var scope = fixture.OpenScope(tenant.Id);
        await Should.ThrowAsync<NotFoundException>(() =>
            scope.Send(new UpdateBotCommand(Guid.NewGuid(), "X", null, null, "Hi", null, "#6366F1", [], false, [], null)));
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Read_only_roles_cannot_update(UserRole role)
    {
        var (tenant, bot) = await SeedBotAsync("Support");

        await using var scope = fixture.OpenScope(tenant.Id, role);
        await Should.ThrowAsync<ForbiddenException>(() => scope.Send(ValidUpdate(bot) with { Name = "Nope" }));
    }

    [Fact]
    public async Task Invalid_settings_are_rejected_with_field_errors()
    {
        var (tenant, bot) = await SeedBotAsync("Support");

        await using var scope = fixture.OpenScope(tenant.Id);
        var error = await Should.ThrowAsync<ValidationException>(() => scope.Send(ValidUpdate(bot) with
        {
            Name = "",
            WelcomeMessage = " ",
            Model = "gpt-4o",
            WidgetColor = "red",
            StarterQuestions = ["1", "2", "3", "4", "5", "6", "7"],
            AllowedDomains = ["acme.com", "https://shop.acme.com/page"],
            DailyMessageLimit = -1,
        }));

        error.Errors.Keys.ShouldBe(
            ["Name", "WelcomeMessage", "Model", "WidgetColor", "StarterQuestions", "AllowedDomains[1]",
                "DailyMessageLimit"],
            ignoreOrder: true);
        error.Errors["WidgetColor"].Length.ShouldBe(1);
    }

    [Fact]
    public async Task At_most_20_websites_are_allowed()
    {
        var (tenant, bot) = await SeedBotAsync("Support");

        await using var scope = fixture.OpenScope(tenant.Id);
        var error = await Should.ThrowAsync<ValidationException>(() => scope.Send(
            ValidUpdate(bot) with { AllowedDomains = [.. Enumerable.Range(1, 21).Select(i => $"site{i}.test")] }));

        error.Errors.Keys.ShouldBe(["AllowedDomains"]);
    }

    [Fact]
    public async Task A_starter_question_longer_than_200_characters_is_rejected()
    {
        var (tenant, bot) = await SeedBotAsync("Support");

        await using var scope = fixture.OpenScope(tenant.Id);
        var error = await Should.ThrowAsync<ValidationException>(
            () => scope.Send(ValidUpdate(bot) with { StarterQuestions = [new string('a', 201)] }));

        error.Errors.Keys.ShouldBe(["StarterQuestions[0]"]);
    }

    // ---------- helpers ----------

    private static UpdateBotCommand ValidUpdate(BotDto bot) => new(
        bot.Id, bot.Name, bot.Description, bot.SystemPrompt, bot.WelcomeMessage, bot.Model, bot.WidgetColor,
        bot.StarterQuestions, bot.IsPublic, bot.AllowedDomains, bot.DailyMessageLimit);

    private async Task<(Tenant Tenant, BotDto Bot)> SeedBotAsync(string botName)
    {
        var tenant = Tenant.Create("Update test company");
        tenant.ChangeSlug($"update-{Guid.NewGuid():N}");

        await using (var system = fixture.OpenScope(tenantId: null))
        {
            system.Db.Tenants.Add(tenant);
            await system.Db.SaveChangesAsync(Ct);
        }

        await using var scope = fixture.OpenScope(tenant.Id);
        var bot = await scope.Send(new CreateBotCommand(botName, null));
        return (tenant, bot);
    }
}
