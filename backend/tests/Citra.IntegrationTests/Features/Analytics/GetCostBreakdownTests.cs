using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Analytics;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Citra.IntegrationTests.Features.Analytics.AnalyticsData;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Analytics;

/// <summary>C-233: AI cost of the period per bot, answers and embeddings apart.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetCostBreakdownTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Splits_each_bots_cost_into_answers_and_embeddings()
    {
        var seed = await SeedAsync(fixture, []);
        var expensive = await AddBotAsync(fixture, seed.Tenant.Id);
        var idle = await AddBotAsync(fixture, seed.Tenant.Id);
        await ChatAsync(fixture, seed, seed.Bot.Id, cost: 0.01m, questions: 2);
        await ChatAsync(fixture, seed, seed.Bot.Id, cost: 0.03m, widget: true);
        await UsageAsync(fixture, seed.Tenant.Id, seed.Bot.Id, 0.005m);
        await ChatAsync(fixture, seed, expensive.Id, cost: 0.10m);
        await MoveBackAsync(fixture, await ChatAsync(fixture, seed, seed.Bot.Id, cost: 1m), days: 10);
        var oldUsage = await UsageAsync(fixture, seed.Tenant.Id, seed.Bot.Id, 2m);
        await using (var system = fixture.OpenScope(tenantId: null))
        {
            await system.Db.EmbeddingUsages.IgnoreQueryFilters().Where(u => u.Id == oldUsage)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CreatedAt, x => x.CreatedAt.AddDays(-10)), Ct);
        }

        var stranger = await SeedAsync(fixture, []);
        await ChatAsync(fixture, stranger, stranger.Bot.Id, cost: 5m);
        await UsageAsync(fixture, stranger.Tenant.Id, stranger.Bot.Id, 5m);

        await using var scope = Scope(fixture, seed);
        var costs = await scope.Send(new GetCostBreakdownQuery(null, Today.AddDays(-6), Today));
        var one = await scope.Send(new GetCostBreakdownQuery(seed.Bot.Id, Today.AddDays(-6), Today));

        costs.Select(c => (c.BotId, c.ChatCostUsd, c.EmbeddingCostUsd)).ShouldBe([
            (expensive.Id, 0.10m, 0m),
            (seed.Bot.Id, 0.05m, 0.005m),
            (idle.Id, 0m, 0m),
        ]);
        costs[0].BotName.ShouldBe("Support");
        one.ShouldBe([new BotCostDto(seed.Bot.Id, "Support", 0.05m, 0.005m)]);
    }

    [Fact]
    public async Task The_period_is_checked()
    {
        var seed = await SeedAsync(fixture, []);

        await using var scope = Scope(fixture, seed);
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetCostBreakdownQuery(null, Today, Today.AddDays(-1))));
    }
}
