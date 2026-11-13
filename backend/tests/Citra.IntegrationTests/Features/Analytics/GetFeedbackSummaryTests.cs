using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Analytics;
using Citra.Domain.Conversations;
using Citra.IntegrationTests.Infrastructure;
using static Citra.IntegrationTests.Features.Analytics.AnalyticsData;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Analytics;

/// <summary>Good and bad ratings of the period's answers, and the badly rated answers with their questions.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetFeedbackSummaryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Counts_ratings_and_lists_the_bad_answers_newest_first()
    {
        var seed = await SeedAsync(fixture, []);
        await ChatAsync(fixture, seed, seed.Bot.Id, question: "Vacation?", feedback: MessageFeedback.Positive, questions: 2);
        var parking = await ChatAsync(fixture, seed, seed.Bot.Id, question: "Parking?", feedback: MessageFeedback.Negative);
        await ChatAsync(fixture, seed, seed.Bot.Id, question: "Unrated?");
        await MoveBackAsync(fixture, await ChatAsync(fixture, seed, seed.Bot.Id, feedback: MessageFeedback.Negative), days: 10);
        var otherBot = await AddBotAsync(fixture, seed.Tenant.Id);
        var gym = await ChatAsync(fixture, seed, otherBot.Id, question: "Gym?", feedback: MessageFeedback.Negative);
        var stranger = await SeedAsync(fixture, []);
        await ChatAsync(fixture, stranger, stranger.Bot.Id, feedback: MessageFeedback.Negative, questions: 3);

        await using var scope = Scope(fixture, seed);
        var bot = await scope.Send(new GetFeedbackSummaryQuery(seed.Bot.Id, Today.AddDays(-6), Today));
        var all = await scope.Send(new GetFeedbackSummaryQuery(null, Today, Today));

        (bot.Positive, bot.Negative).ShouldBe((2, 1));
        bot.BadAnswers.Select(a => (a.ConversationId, a.BotId, a.Question, a.Answer))
            .ShouldBe([(parking, seed.Bot.Id, "Parking?", "Answer.")]);
        (all.Positive, all.Negative).ShouldBe((2, 2));
        all.BadAnswers.Select(a => a.ConversationId).ShouldBe([gym, parking]);
    }

    [Fact]
    public async Task Lists_at_most_50_bad_answers_and_checks_the_period()
    {
        var seed = await SeedAsync(fixture, []);
        await ChatAsync(fixture, seed, seed.Bot.Id, feedback: MessageFeedback.Negative, questions: 51);

        await using var scope = Scope(fixture, seed);
        var summary = await scope.Send(new GetFeedbackSummaryQuery(null, Today, Today));

        summary.Negative.ShouldBe(51);
        summary.BadAnswers.Count.ShouldBe(50);
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetFeedbackSummaryQuery(null, Today, Today.AddDays(-1))));
    }
}
