using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Analytics;
using Citra.IntegrationTests.Infrastructure;
using static Citra.IntegrationTests.Features.Analytics.AnalyticsData;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Analytics;

/// <summary>C-231: the most asked questions, with wordings that differ only in case or punctuation counted together.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class GetTopQuestionsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Groups_the_same_question_and_shows_its_most_common_wording()
    {
        var seed = await SeedAsync(fixture, []);
        await ChatAsync(fixture, seed, seed.Bot.Id, question: "How many vacation days?", questions: 2);
        await ChatAsync(fixture, seed, seed.Bot.Id, question: "  how many   VACATION days ", widget: true);
        await ChatAsync(fixture, seed, seed.Bot.Id, question: "How many vacation days!");
        await ChatAsync(fixture, seed, seed.Bot.Id, question: "Kako se prijaviti na DLWMS?", questions: 2);
        await ChatAsync(fixture, seed, seed.Bot.Id, question: "kako se prijaviti na dlwms");
        await ChatAsync(fixture, seed, seed.Bot.Id, question: "Where do I park?");
        await ChatAsync(fixture, seed, seed.Bot.Id, question: "Can I bring my dog?");
        await MoveBackAsync(fixture, await ChatAsync(fixture, seed, seed.Bot.Id, question: "Where do I park?", questions: 9), days: 10);
        var otherBot = await AddBotAsync(fixture, seed.Tenant.Id);
        await ChatAsync(fixture, seed, otherBot.Id, question: "Where do I park?", questions: 5);
        var stranger = await SeedAsync(fixture, []);
        await ChatAsync(fixture, stranger, stranger.Bot.Id, question: "Can I bring my dog?", questions: 9);

        await using var scope = Scope(fixture, seed);
        var top = await scope.Send(new GetTopQuestionsQuery(seed.Bot.Id, Today.AddDays(-6), Today));
        var two = await scope.Send(new GetTopQuestionsQuery(seed.Bot.Id, Today.AddDays(-6), Today, Limit: 2));
        var all = await scope.Send(new GetTopQuestionsQuery(null, Today, Today, Limit: 1));

        top.ShouldBe([
            new TopQuestionDto("How many vacation days?", 4),
            new TopQuestionDto("Kako se prijaviti na DLWMS?", 3),
            new TopQuestionDto("Can I bring my dog?", 1),
            new TopQuestionDto("Where do I park?", 1),
        ]);
        two.Count.ShouldBe(2);
        all.ShouldBe([new TopQuestionDto("Where do I park?", 6)]);
    }

    [Fact]
    public async Task The_limit_and_the_period_are_checked()
    {
        var seed = await SeedAsync(fixture, []);

        await using var scope = Scope(fixture, seed);
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetTopQuestionsQuery(null, Today, Today, Limit: 0)));
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetTopQuestionsQuery(null, Today, Today, Limit: 51)));
        await Should.ThrowAsync<ValidationException>(() => scope.Send(new GetTopQuestionsQuery(null, Today, Today.AddDays(-1))));
    }
}
