using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Bots.Create;
using Citra.Domain.Bots;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Features.Bots;

[Collection(DatabaseCollection.Name)]
public sealed class CreateBotTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creates_a_bot_in_the_current_tenant_with_default_settings()
    {
        var tenant = await SeedTenantAsync();

        await using var asAdmin = fixture.OpenScope(tenant.Id);
        var created = await asAdmin.Send(new CreateBotCommand("  Customer Support  ", "Answers product questions"));

        created.Name.ShouldBe("Customer Support");
        created.Slug.ShouldBe("customer-support");
        created.Description.ShouldBe("Answers product questions");
        created.WelcomeMessage.ShouldBe(Bot.DefaultWelcomeMessage);
        created.WidgetColor.ShouldBe(Bot.DefaultWidgetColor);
        created.IsPublic.ShouldBeFalse("a new bot must not be reachable from the public widget");
        created.CreatedAt.ShouldNotBe(default);

        await using var check = fixture.OpenScope(tenant.Id);
        var saved = await check.Db.Bots.SingleAsync(b => b.Id == created.Id, Ct);
        saved.TenantId.ShouldBe(tenant.Id);
    }

    [Fact]
    public async Task Same_name_in_the_same_tenant_gets_a_numbered_slug()
    {
        var tenant = await SeedTenantAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        var first = await scope.Send(new CreateBotCommand("Support", null));
        var second = await scope.Send(new CreateBotCommand("Support", null));
        var third = await scope.Send(new CreateBotCommand("support!", null));

        first.Slug.ShouldBe("support");
        second.Slug.ShouldBe("support-2");
        third.Slug.ShouldBe("support-3");
    }

    [Fact]
    public async Task Different_tenants_can_use_the_same_slug()
    {
        var a = await SeedTenantAsync();
        var b = await SeedTenantAsync();

        await using var asA = fixture.OpenScope(a.Id);
        var botA = await asA.Send(new CreateBotCommand("Helpdesk", null));
        await using var asB = fixture.OpenScope(b.Id);
        var botB = await asB.Send(new CreateBotCommand("Helpdesk", null));

        botA.Slug.ShouldBe("helpdesk");
        botB.Slug.ShouldBe("helpdesk", "another company's bot must not affect our slugs");
    }

    [Theory]
    [InlineData(UserRole.Viewer)]
    [InlineData(UserRole.Demo)]
    public async Task Read_only_roles_cannot_create_bots(UserRole role)
    {
        var tenant = await SeedTenantAsync();

        await using var scope = fixture.OpenScope(tenant.Id, role);
        await Should.ThrowAsync<ForbiddenException>(() => scope.Send(new CreateBotCommand("Nope", null)));

        (await scope.Db.Bots.AnyAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Owner_can_create_bots()
    {
        var tenant = await SeedTenantAsync();

        await using var scope = fixture.OpenScope(tenant.Id, UserRole.Owner);
        var created = await scope.Send(new CreateBotCommand("Owner bot", null));

        created.Slug.ShouldBe("owner-bot");
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected()
    {
        await using var anonymous = fixture.OpenScope(tenantId: null);

        await Should.ThrowAsync<UnauthorizedException>(() => anonymous.Send(new CreateBotCommand("Nope", null)));
    }

    [Fact]
    public async Task Invalid_input_is_rejected_with_field_errors()
    {
        var tenant = await SeedTenantAsync();

        await using var scope = fixture.OpenScope(tenant.Id);
        var error = await Should.ThrowAsync<ValidationException>(() => scope.Send(
            new CreateBotCommand("   ", new string('x', Bot.DescriptionMaxLength + 1))));

        error.Errors.Keys.ShouldBe(["Name", "Description"], ignoreOrder: true);
        (await scope.Db.Bots.AnyAsync(Ct)).ShouldBeFalse();
    }

    // ---------- helpers ----------

    private async Task<Tenant> SeedTenantAsync()
    {
        var tenant = Tenant.Create("Bots test company");
        tenant.ChangeSlug($"bots-{Guid.NewGuid():N}");

        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.Tenants.Add(tenant);
        await system.Db.SaveChangesAsync(Ct);
        return tenant;
    }
}
