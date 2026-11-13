using Citra.Application.Common.Embeddings;
using Citra.Domain.Bots;
using Citra.Domain.Documents;
using Citra.Domain.Tenants;
using Citra.Domain.Usage;
using Citra.Infrastructure.Embeddings;
using Citra.Infrastructure.Options;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Citra.IntegrationTests.Embeddings;

/// <summary>Embedding token usage and cost, recorded for the cost dashboard.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class EmbeddingUsageTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- cost ----------

    [Theory]
    [InlineData(0, "0.06", "0")]
    [InlineData(1_000_000, "0.06", "0.06")]
    [InlineData(4, "0.06", "0.00000024")]
    [InlineData(123_456, "0.06", "0.00740736")]
    [InlineData(200_000_000, "0.06", "12")]
    [InlineData(1_000, "0", "0")]
    public void Cost_is_exact(int tokens, string price, string expected) =>
        EmbeddingCost.Calculate(tokens, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Negative_amounts_are_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => EmbeddingCost.Calculate(-1, 0.06m));
        Should.Throw<ArgumentOutOfRangeException>(() => EmbeddingCost.Calculate(1, -0.06m));
    }

    [Fact]
    public void The_embedding_service_reports_its_configured_price()
    {
        var service = new VoyageEmbeddingService(
            new HttpClient(),
            Microsoft.Extensions.Options.Options.Create(new VoyageOptions { PricePerMillionTokensUsd = 0.06m }),
            new Citra.Infrastructure.Chunking.TiktokenTokenCounter());

        service.PricePerMillionTokensUsd.ShouldBe(0.06m);
    }

    // ---------- the record ----------

    [Fact]
    public void Records_what_was_embedded()
    {
        var (tenant, bot, document) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var forDocument = EmbeddingUsage.ForDocument(tenant, bot, document, " voyage-3.5 ", 1_200, 0.000072m);
        var forQuery = EmbeddingUsage.ForQuery(tenant, bot, "voyage-3.5", 9, 0.00000054m);

        (forDocument.TenantId, forDocument.BotId, forDocument.DocumentId, forDocument.Purpose, forDocument.Model, forDocument.Tokens, forDocument.CostUsd)
            .ShouldBe((tenant, bot, (Guid?)document, EmbeddingPurpose.Document, "voyage-3.5", 1_200, 0.000072m));
        (forQuery.DocumentId, forQuery.Purpose, forQuery.Tokens).ShouldBe(((Guid?)null, EmbeddingPurpose.Query, 9));
    }

    [Fact]
    public void Invalid_records_are_refused()
    {
        var (tenant, bot, document) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Should.Throw<ArgumentException>(() => EmbeddingUsage.ForQuery(Guid.Empty, bot, "m", 1, 0));
        Should.Throw<ArgumentException>(() => EmbeddingUsage.ForQuery(tenant, Guid.Empty, "m", 1, 0));
        Should.Throw<ArgumentException>(() => EmbeddingUsage.ForDocument(tenant, bot, Guid.Empty, "m", 1, 0));
        Should.Throw<ArgumentException>(() => EmbeddingUsage.ForQuery(tenant, bot, " ", 1, 0));
        Should.Throw<ArgumentException>(() => EmbeddingUsage.ForQuery(tenant, bot, new string('m', 101), 1, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => EmbeddingUsage.ForQuery(tenant, bot, "m", -1, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => EmbeddingUsage.ForDocument(tenant, bot, document, "m", 1, -0.01m));
    }

    // ---------- in the database ----------

    [Fact]
    public async Task Tiny_costs_are_stored_without_rounding_away()
    {
        var seed = await SeedAsync();
        var question = EmbeddingUsage.ForQuery(seed.Tenant.Id, seed.Bot.Id, "voyage-3.5", 4, EmbeddingCost.Calculate(4, 0.06m));
        await SaveAsSystemAsync(question);

        await using var scope = fixture.OpenScope(seed.Tenant.Id);
        var stored = await scope.Db.EmbeddingUsages.SingleAsync(u => u.Id == question.Id, Ct);

        stored.CostUsd.ShouldBe(0.00000024m);
        stored.CreatedAt.ShouldNotBe(default);
    }

    [Fact]
    public async Task Another_company_never_sees_the_usage()
    {
        var mine = await SeedAsync();
        var theirs = await SeedAsync();
        await SaveAsSystemAsync(EmbeddingUsage.ForQuery(theirs.Tenant.Id, theirs.Bot.Id, "voyage-3.5", 10, 0.0000006m));

        await using var scope = fixture.OpenScope(mine.Tenant.Id);
        (await scope.Db.EmbeddingUsages.AnyAsync(Ct)).ShouldBeFalse();

        scope.Db.EmbeddingUsages.Add(EmbeddingUsage.ForQuery(theirs.Tenant.Id, theirs.Bot.Id, "voyage-3.5", 1, 0));
        (await Should.ThrowAsync<InvalidOperationException>(() => scope.Db.SaveChangesAsync(Ct))).Message.ShouldContain("cross-tenant");
    }

    [Fact]
    public async Task Usage_stays_when_its_document_is_deleted()
    {
        var seed = await SeedAsync();
        var usage = EmbeddingUsage.ForDocument(seed.Tenant.Id, seed.Bot.Id, seed.Document.Id, "voyage-3.5", 5_000, 0.0003m);
        await SaveAsSystemAsync(usage);

        await using (var scope = fixture.OpenScope(seed.Tenant.Id))
        {
            await scope.Db.Documents.Where(d => d.Id == seed.Document.Id).ExecuteDeleteAsync(Ct);
        }

        await using var check = fixture.OpenScope(seed.Tenant.Id);
        var kept = await check.Db.EmbeddingUsages.SingleAsync(u => u.Id == usage.Id, Ct);
        kept.DocumentId.ShouldBeNull();
        kept.BotId.ShouldBe(seed.Bot.Id);
        kept.CostUsd.ShouldBe(0.0003m, "the money was spent even though the document is gone");
    }

    [Fact]
    public async Task Usage_stays_when_its_bot_is_deleted()
    {
        var seed = await SeedAsync();
        var usage = EmbeddingUsage.ForDocument(seed.Tenant.Id, seed.Bot.Id, seed.Document.Id, "voyage-3.5", 5_000, 0.0003m);
        await SaveAsSystemAsync(usage);

        await using (var scope = fixture.OpenScope(seed.Tenant.Id))
        {
            await scope.Db.Bots.Where(b => b.Id == seed.Bot.Id).ExecuteDeleteAsync(Ct);
        }

        await using var check = fixture.OpenScope(seed.Tenant.Id);
        var kept = await check.Db.EmbeddingUsages.SingleAsync(u => u.Id == usage.Id, Ct);
        (kept.BotId, kept.DocumentId).ShouldBe(((Guid?)null, (Guid?)null));
        kept.Tokens.ShouldBe(5_000);
    }

    [Fact]
    public async Task Deleting_a_company_removes_its_usage()
    {
        var seed = await SeedAsync();
        var usage = EmbeddingUsage.ForQuery(seed.Tenant.Id, seed.Bot.Id, "voyage-3.5", 3, 0);
        await SaveAsSystemAsync(usage);

        await using var system = fixture.OpenScope(tenantId: null);
        await system.Db.Tenants.Where(t => t.Id == seed.Tenant.Id).ExecuteDeleteAsync(Ct);

        (await system.Db.EmbeddingUsages.IgnoreQueryFilters().AnyAsync(u => u.Id == usage.Id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Supports_the_dashboards_cost_per_bot_and_purpose()
    {
        var seed = await SeedAsync();
        var otherBot = Bot.Create(seed.Tenant.Id, "HR");
        await SaveAsSystemAsync(otherBot);
        await SaveAsSystemAsync(
            EmbeddingUsage.ForDocument(seed.Tenant.Id, seed.Bot.Id, seed.Document.Id, "voyage-3.5", 10_000, EmbeddingCost.Calculate(10_000, 0.06m)),
            EmbeddingUsage.ForQuery(seed.Tenant.Id, seed.Bot.Id, "voyage-3.5", 4, EmbeddingCost.Calculate(4, 0.06m)),
            EmbeddingUsage.ForQuery(seed.Tenant.Id, seed.Bot.Id, "voyage-3.5", 6, EmbeddingCost.Calculate(6, 0.06m)),
            EmbeddingUsage.ForQuery(seed.Tenant.Id, otherBot.Id, "voyage-3.5", 20, EmbeddingCost.Calculate(20, 0.06m)));

        await using var scope = fixture.OpenScope(seed.Tenant.Id);
        var since = DateTimeOffset.UtcNow.AddHours(-1);
        var breakdown = await scope.Db.EmbeddingUsages
            .Where(u => u.CreatedAt >= since)
            .GroupBy(u => new { u.BotId, u.Purpose })
            .Select(g => new { g.Key.BotId, g.Key.Purpose, Tokens = g.Sum(u => u.Tokens), Cost = g.Sum(u => u.CostUsd) })
            .ToListAsync(Ct);

        var supportQueries = breakdown.Single(b => b.BotId == seed.Bot.Id && b.Purpose == EmbeddingPurpose.Query);
        (supportQueries.Tokens, supportQueries.Cost).ShouldBe((10, 0.0000006m), "tiny costs add up exactly");
        breakdown.Single(b => b.BotId == seed.Bot.Id && b.Purpose == EmbeddingPurpose.Document).Cost.ShouldBe(0.0006m);
        breakdown.Single(b => b.BotId == otherBot.Id).Cost.ShouldBe(0.0000012m);
    }

    // ---------- helpers ----------

    private sealed record Seed(Tenant Tenant, Bot Bot, Document Document);

    private async Task<Seed> SeedAsync()
    {
        var tenant = Tenant.Create("Usage company");
        tenant.ChangeSlug($"usage-{Guid.NewGuid():N}");
        var bot = Bot.Create(tenant.Id, "Support");
        var document = Document.FromUpload(
            tenant.Id, bot.Id, "Handbook.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{tenant.Id}/{bot.Id}/{Guid.NewGuid():N}/original.pdf", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
        await SaveAsSystemAsync(tenant, bot, document);
        return new Seed(tenant, bot, document);
    }

    private async Task SaveAsSystemAsync(params object[] entities)
    {
        await using var system = fixture.OpenScope(tenantId: null);
        system.Db.AddRange(entities);
        await system.Db.SaveChangesAsync(Ct);
    }
}
