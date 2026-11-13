using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Analytics;
using Citra.IntegrationTests.Infrastructure;
using static Citra.IntegrationTests.Features.Analytics.AnalyticsData;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Analytics;

/// <summary>Questions per day for the dashboard chart.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetMessagesPerDayTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Counts_questions_per_day_with_empty_days_as_zero()
    {
        var seed = await SeedAsync(fixture, []);
        var otherBot = await AddBotAsync(fixture, seed.Tenant.Id);
        await ChatAsync(fixture, seed, seed.Bot.Id, questions: 2);
        await ChatAsync(fixture, seed, seed.Bot.Id, widget: true);
        await MoveBackAsync(fixture, await ChatAsync(fixture, seed, seed.Bot.Id), days: 2);
        await MoveBackAsync(fixture, await ChatAsync(fixture, seed, seed.Bot.Id), days: 5);
        await ChatAsync(fixture, seed, otherBot.Id);
        var stranger = await SeedAsync(fixture, []);
        await ChatAsync(fixture, stranger, stranger.Bot.Id, questions: 4);

        await using var scope = Scope(fixture, seed);
        var bot = await scope.Send(new GetMessagesPerDayQuery(seed.Bot.Id, Today.AddDays(-3), Today));
        var all = await scope.Send(new GetMessagesPerDayQuery(null, Today, Today));
        var past = await scope.Send(new GetMessagesPerDayQuery(seed.Bot.Id, Today.AddDays(-2), Today.AddDays(-1)));

        bot.ShouldBe([
            new DayCountDto(Today.AddDays(-3), 0),
            new DayCountDto(Today.AddDays(-2), 1),
            new DayCountDto(Today.AddDays(-1), 0),
            new DayCountDto(Today, 3),
        ]);
        all.ShouldBe([new DayCountDto(Today, 4)]);
        past.ShouldBe([new DayCountDto(Today.AddDays(-2), 1), new DayCountDto(Today.AddDays(-1), 0)]);
    }

    [Fact]
    public async Task The_period_must_run_forwards_and_be_at_most_a_year()
    {
        var seed = await SeedAsync(fixture, []);

        await using var scope = Scope(fixture, seed);
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetMessagesPerDayQuery(null, Today, Today.AddDays(-1))));
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetMessagesPerDayQuery(null, Today.AddDays(-366), Today)));
    }
}
