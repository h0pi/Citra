using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Evaluation;
using Citra.Domain.Users;
using Citra.IntegrationTests.Features.Chat;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Evaluation;

/// <summary>Eval sets and their test questions — created, read, changed and deleted per bot.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class EvalSetTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creates_lists_renames_and_deletes_a_bots_eval_sets()
    {
        var seed = await SeedAsync(fixture, []);
        var otherBot = await AddBotAsync(fixture, seed.Tenant.Id);
        await using var scope = Scope(fixture, seed);

        var first = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "  HR basics ", " Leave rules "));
        var second = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "Parking", null));
        await scope.Send(new CreateEvalSetCommand(otherBot.Id, "Other bot", null));
        await scope.Send(new AddEvalQuestionCommand(first.Id, "Vacation days?", "25 days.", null));

        (first.Name, first.Description, first.BotId).ShouldBe(("HR basics", "Leave rules", seed.Bot.Id));
        var sets = await scope.Send(new GetEvalSetsQuery(seed.Bot.Id));
        sets.Select(s => (s.Id, s.Name, s.QuestionCount)).ShouldBe([(second.Id, "Parking", 0), (first.Id, "HR basics", 1)]);

        var renamed = await scope.Send(new UpdateEvalSetCommand(first.Id, "HR", ""));
        (renamed.Name, renamed.Description).ShouldBe(("HR", null));

        await scope.Send(new DeleteEvalSetCommand(first.Id));
        await Should.ThrowAsync<NotFoundException>(() => scope.Send(new GetEvalSetQuery(first.Id)));
        (await scope.Db.EvalQuestions.CountAsync(q => q.EvalSetId == first.Id, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Adds_changes_and_removes_questions_with_an_expected_document_of_the_bot()
    {
        var seed = await SeedAsync(fixture, []);
        var otherBot = await AddBotAsync(fixture, seed.Tenant.Id);
        await using var scope = Scope(fixture, seed);
        var handbook = await scope.Db.Documents.Where(d => d.BotId == seed.Bot.Id).Select(d => d.Id).SingleAsync(Ct);
        var otherDocument = await scope.Db.Documents.Where(d => d.BotId == otherBot.Id).Select(d => d.Id).SingleAsync(Ct);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));

        var vacation = await scope.Send(new AddEvalQuestionCommand(set.Id, " Vacation days? ", " 25 days. ", handbook));
        var parking = await scope.Send(new AddEvalQuestionCommand(set.Id, "Parking?", "Behind the office.", null));
        (await scope.Send(new GetEvalSetQuery(set.Id))).Questions.Select(q => q.Id).ShouldBe([vacation.Id, parking.Id]);
        await scope.Send(new UpdateEvalQuestionCommand(set.Id, parking.Id, "Where do I park?", "Behind the office.", handbook));
        await scope.Send(new DeleteEvalQuestionCommand(set.Id, vacation.Id));

        (await scope.Send(new GetEvalSetQuery(set.Id))).Questions
            .ShouldBe([new EvalQuestionDto(parking.Id, "Where do I park?", "Behind the office.", handbook)]);
        vacation.ShouldBe(new EvalQuestionDto(vacation.Id, "Vacation days?", "25 days.", handbook));

        var error = await Should.ThrowAsync<ValidationException>(
            () => scope.Send(new AddEvalQuestionCommand(set.Id, "Gym?", "No.", otherDocument)));
        error.Errors.Keys.ShouldBe(["ExpectedDocumentId"]);
        await Should.ThrowAsync<NotFoundException>(
            () => scope.Send(new UpdateEvalQuestionCommand(set.Id, Guid.NewGuid(), "Q?", "A.", null)));
    }

    [Fact]
    public async Task Viewers_only_read_and_other_companies_sets_are_not_found()
    {
        var seed = await SeedAsync(fixture, []);
        var viewer = seed with { UserId = await CreateUserAsync(fixture, seed.Tenant.Id, UserRole.Viewer), Role = UserRole.Viewer };
        var stranger = await SeedAsync(fixture, []);
        EvalSetDto set;
        await using (var scope = Scope(fixture, seed))
        {
            set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        }

        await using (var asViewer = Scope(fixture, viewer))
        {
            (await asViewer.Send(new GetEvalSetQuery(set.Id))).Name.ShouldBe("HR");
            await Should.ThrowAsync<ForbiddenException>(() => asViewer.Send(new CreateEvalSetCommand(seed.Bot.Id, "X", null)));
            await Should.ThrowAsync<ForbiddenException>(() => asViewer.Send(new AddEvalQuestionCommand(set.Id, "Q?", "A.", null)));
            await Should.ThrowAsync<ForbiddenException>(() => asViewer.Send(new DeleteEvalSetCommand(set.Id)));
        }

        await using var asStranger = Scope(fixture, stranger);
        await Should.ThrowAsync<NotFoundException>(() => asStranger.Send(new GetEvalSetQuery(set.Id)));
        await Should.ThrowAsync<NotFoundException>(() => asStranger.Send(new GetEvalSetsQuery(seed.Bot.Id)));
        await Should.ThrowAsync<NotFoundException>(() => asStranger.Send(new CreateEvalSetCommand(seed.Bot.Id, "X", null)));
        await Should.ThrowAsync<NotFoundException>(() => asStranger.Send(new AddEvalQuestionCommand(set.Id, "Q?", "A.", null)));
    }

    [Fact]
    public async Task Invalid_names_and_questions_are_rejected()
    {
        var seed = await SeedAsync(fixture, []);
        await using var scope = Scope(fixture, seed);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));

        var setError = await Should.ThrowAsync<ValidationException>(
            () => scope.Send(new CreateEvalSetCommand(seed.Bot.Id, " ", new string('d', 501))));
        setError.Errors.Keys.ShouldBe(["Name", "Description"], ignoreOrder: true);
        var renameError = await Should.ThrowAsync<ValidationException>(
            () => scope.Send(new UpdateEvalSetCommand(set.Id, new string('n', 101), null)));
        renameError.Errors.Keys.ShouldBe(["Name"]);
        var questionError = await Should.ThrowAsync<ValidationException>(
            () => scope.Send(new AddEvalQuestionCommand(set.Id, new string('q', 1001), "", null)));
        questionError.Errors.Keys.ShouldBe(["Question", "ExpectedAnswer"], ignoreOrder: true);
        var updateError = await Should.ThrowAsync<ValidationException>(
            () => scope.Send(new UpdateEvalQuestionCommand(set.Id, Guid.NewGuid(), "", new string('a', 4001), null)));
        updateError.Errors.Keys.ShouldBe(["Question", "ExpectedAnswer"], ignoreOrder: true);
    }
}
