using System.Net;
using System.Net.Http.Json;
using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Analytics;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Citra.IntegrationTests.Features.Analytics.AnalyticsData;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Analytics;

/// <summary>The dashboard's headline numbers for a period, per bot or for all bots.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetDashboardStatsTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Counts_chats_questions_answers_latency_and_cost_of_the_period()
    {
        var seed = await SeedAsync(fixture, []);
        var otherBot = await AddBotAsync(fixture, seed.Tenant.Id);
        await ChatAsync(fixture, seed, seed.Bot.Id, answered: true, latencyMs: 1000, cost: 0.01m);
        await ChatAsync(fixture, seed, seed.Bot.Id, answered: false, latencyMs: 3000, cost: 0.02m, widget: true);
        await ChatAsync(fixture, seed, otherBot.Id, answered: true, latencyMs: 5000, cost: 0.10m);
        var old = await ChatAsync(fixture, seed, seed.Bot.Id, answered: true, latencyMs: 9000, cost: 1m);
        await MoveBackAsync(fixture, old, days: 10);
        await UsageAsync(fixture, seed.Tenant.Id, seed.Bot.Id, 0.005m);
        var stranger = await SeedAsync(fixture, []);
        await ChatAsync(fixture, stranger, stranger.Bot.Id, answered: true, latencyMs: 7000, cost: 5m);

        await using var scope = Scope(fixture, seed);
        var bot = await scope.Send(new GetDashboardStatsQuery(seed.Bot.Id, Today.AddDays(-6), Today));
        var all = await scope.Send(new GetDashboardStatsQuery(null, Today.AddDays(-6), Today));
        var longer = await scope.Send(new GetDashboardStatsQuery(seed.Bot.Id, Today.AddDays(-29), Today));
        var past = await scope.Send(new GetDashboardStatsQuery(seed.Bot.Id, Today.AddDays(-29), Today.AddDays(-1)));

        bot.ShouldBe(new DashboardStatsDto(2, 2, 50, 2000, 0.035m));
        all.ShouldBe(new DashboardStatsDto(3, 3, 66.7, 3000, 0.135m));
        longer.ShouldBe(new DashboardStatsDto(3, 3, 66.7, 4333, 1.035m));
        past.ShouldBe(new DashboardStatsDto(1, 1, 100, 9000, 1m));
    }

    [Fact]
    public async Task A_period_without_chats_has_zeros()
    {
        var seed = await SeedAsync(fixture, []);

        await using var scope = Scope(fixture, seed);
        var stats = await scope.Send(new GetDashboardStatsQuery(null, Today, Today));

        stats.ShouldBe(new DashboardStatsDto(0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task The_period_must_run_forwards_and_be_at_most_a_year()
    {
        var seed = await SeedAsync(fixture, []);

        await using var scope = Scope(fixture, seed);
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetDashboardStatsQuery(null, Today, Today.AddDays(-1))));
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetDashboardStatsQuery(null, Today.AddDays(-366), Today)));
    }

    [Fact]
    public async Task The_endpoint_needs_a_login()
    {
        await using var api = new ApiFactory(fixture);
        using var response = await api.CreateClient().GetAsync($"/api/analytics/stats?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
