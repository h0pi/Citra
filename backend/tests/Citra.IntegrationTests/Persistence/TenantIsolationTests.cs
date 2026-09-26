using Citra.Application.Common.Persistence;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Persistence;

/// <summary>
/// Tenant isolation is Citra's most important security property: one company must never see or change
/// another company's data. These tests pin that behaviour down at the database layer.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TenantIsolationTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenant_sees_only_its_own_bots()
    {
        var (a, b) = await SeedTwoTenantsAsync();

        await using var asA = fixture.OpenScope(a.Tenant.Id);
        var botIds = await asA.Db.Bots.Select(x => x.Id).ToListAsync(Ct);

        botIds.ShouldBe([a.Bot.Id], ignoreOrder: true);
        botIds.ShouldNotContain(b.Bot.Id);
    }

    [Fact]
    public async Task Tenant_cannot_read_another_tenants_bot_by_id()
    {
        var (a, b) = await SeedTwoTenantsAsync();

        await using var asA = fixture.OpenScope(a.Tenant.Id);
        var stolen = await asA.Db.Bots.FirstOrDefaultAsync(x => x.Id == b.Bot.Id, Ct);

        stolen.ShouldBeNull();
    }

    [Fact]
    public async Task Tenant_cannot_see_another_tenants_documents()
    {
        var (a, b) = await SeedTwoTenantsAsync();

        await using var asA = fixture.OpenScope(a.Tenant.Id);
        var visible = await asA.Db.Documents.Select(d => d.Id).ToListAsync(Ct);

        visible.ShouldContain(a.Document.Id);
        visible.ShouldNotContain(b.Document.Id);
    }

    [Fact]
    public async Task Without_a_logged_in_user_queries_return_nothing()
    {
        var (a, b) = await SeedTwoTenantsAsync();

        await using var anonymous = fixture.OpenScope(tenantId: null);
        var visible = await anonymous.Db.Bots
            .Where(x => x.Id == a.Bot.Id || x.Id == b.Bot.Id)
            .CountAsync(Ct);

        visible.ShouldBe(0, "the filter must fail closed, never open");
    }

    [Fact]
    public async Task Explicitly_ignoring_the_tenant_filter_sees_every_tenant()
    {
        var (a, b) = await SeedTwoTenantsAsync();

        await using var system = fixture.OpenScope(tenantId: null);
        var visible = await system.Db.Bots
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(x => x.Id == a.Bot.Id || x.Id == b.Bot.Id)
            .CountAsync(Ct);

        visible.ShouldBe(2);
    }

    [Fact]
    public async Task Tenant_cannot_create_data_inside_another_tenant()
    {
        var (a, b) = await SeedTwoTenantsAsync();

        await using var asA = fixture.OpenScope(a.Tenant.Id);
        asA.Db.Bots.Add(Bot.Create(b.Tenant.Id, "Sneaky bot"));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => asA.Db.SaveChangesAsync(Ct));
        error.Message.ShouldContain("cross-tenant");
    }

    [Fact]
    public async Task Tenant_cannot_modify_another_tenants_data_even_if_loaded_around_the_filter()
    {
        var (a, b) = await SeedTwoTenantsAsync();

        await using var asA = fixture.OpenScope(a.Tenant.Id);
        var foreignBot = await asA.Db.Bots.IgnoreQueryFilters().SingleAsync(x => x.Id == b.Bot.Id, Ct);
        foreignBot.UpdateDetails("Hacked", null, null, "hi");

        await Should.ThrowAsync<InvalidOperationException>(() => asA.Db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Tenant_cannot_delete_another_tenants_data()
    {
        var (a, b) = await SeedTwoTenantsAsync();

        await using var asA = fixture.OpenScope(a.Tenant.Id);
        var foreignBot = await asA.Db.Bots.IgnoreQueryFilters().SingleAsync(x => x.Id == b.Bot.Id, Ct);
        asA.Db.Bots.Remove(foreignBot);

        await Should.ThrowAsync<InvalidOperationException>(() => asA.Db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Bulk_delete_only_affects_the_current_tenant()
    {
        var (a, b) = await SeedTwoTenantsAsync();

        await using (var asA = fixture.OpenScope(a.Tenant.Id))
        {
            await asA.Db.Bots.ExecuteDeleteAsync(Ct);
        }

        await using var asB = fixture.OpenScope(b.Tenant.Id);
        (await asB.Db.Bots.AnyAsync(x => x.Id == b.Bot.Id, Ct)).ShouldBeTrue("B's bot must survive A's bulk delete");
    }

    [Fact]
    public async Task Tenant_can_create_and_change_its_own_data()
    {
        var (a, _) = await SeedTwoTenantsAsync();

        await using var asA = fixture.OpenScope(a.Tenant.Id);
        asA.Db.Bots.Add(Bot.Create(a.Tenant.Id, "New bot"));
        var bot = await asA.Db.Bots.SingleAsync(x => x.Id == a.Bot.Id, Ct);
        bot.UpdateDetails("Renamed", null, null, "hi");

        (await asA.Db.SaveChangesAsync(Ct)).ShouldBe(2);
    }

    // ---------- helpers ----------

    private sealed record TenantData(Tenant Tenant, Bot Bot, Document Document);

    /// <summary>Two fresh tenants, each with a bot and a document. Saved without a user, like sign-up does.</summary>
    private async Task<(TenantData A, TenantData B)> SeedTwoTenantsAsync()
    {
        var a = NewTenant("Company A");
        var b = NewTenant("Company B");

        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(a.Tenant, a.Bot, a.Document, b.Tenant, b.Bot, b.Document);
        await system.Db.SaveChangesAsync(Ct);

        return (a, b);
    }

    private static TenantData NewTenant(string name)
    {
        var tenant = Tenant.Create(name);
        tenant.ChangeSlug($"{name}-{Guid.NewGuid():N}"); // unique across tests sharing the database
        var bot = Bot.Create(tenant.Id, $"{name} bot");
        var document = Document.FromUpload(
            tenant.Id, bot.Id, $"{name}.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenant.Id}/{bot.Id}/file.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));

        return new TenantData(tenant, bot, document);
    }
}
