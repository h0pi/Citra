using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Evaluation;
using Citra.Domain.Users;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Evaluation;

/// <summary>C-241: test questions imported from a CSV file, all or nothing, with errors naming the rows.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class EvalImportTests(DatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Imports_quoted_values_and_finds_the_expected_document_by_file_name()
    {
        var seed = await SeedAsync(fixture, []);
        await using var scope = Scope(fixture, seed);
        var handbook = await scope.Db.Documents.Where(d => d.BotId == seed.Bot.Id).Select(d => d.Id).SingleAsync(Ct);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        await scope.Send(new AddEvalQuestionCommand(set.Id, "Existing?", "Yes.", null));
        var csv = "﻿Question,Expected_Answer,Expected_Document\r\n"
            + "How many vacation days?,\"25 days, plus holidays.\",handbook.PDF\r\n"
            + "\r\n"
            + "\"Two\nlines?\",\"He said \"\"yes\"\".\",\r\n"
            + "Parking?,Behind the office.";

        var imported = await scope.Send(new ImportEvalQuestionsCommand(set.Id, csv));

        imported.Questions.Select(q => (q.Question, q.ExpectedAnswer, q.ExpectedDocumentId)).ShouldBe([
            ("Existing?", "Yes.", null),
            ("How many vacation days?", "25 days, plus holidays.", handbook),
            ("Two\nlines?", "He said \"yes\".", null),
            ("Parking?", "Behind the office.", null),
        ]);
        (await scope.Db.EvalQuestions.Where(q => q.EvalSetId == set.Id).OrderBy(q => q.Position).Select(q => q.Position).ToListAsync(Ct))
            .ShouldBe([0, 1, 2, 3]);
    }

    [Fact]
    public async Task Reads_semicolon_files_from_excel()
    {
        var seed = await SeedAsync(fixture, []);
        await using var scope = Scope(fixture, seed);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));

        var imported = await scope.Send(new ImportEvalQuestionsCommand(set.Id, "question;expected_answer\nKoliko dana odmora?;25 dana, plus praznici.\n"));

        imported.Questions.Select(q => (q.Question, q.ExpectedAnswer)).ShouldBe([("Koliko dana odmora?", "25 dana, plus praznici.")]);
    }

    [Fact]
    public async Task Adds_nothing_when_a_row_is_wrong_and_names_the_rows()
    {
        var seed = await SeedAsync(fixture, []);
        await using var scope = Scope(fixture, seed);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        var csv = "question,expected_answer,expected_document\n"
            + "Fine?,Yes.,\n"
            + ",No question.,\n"
            + "No answer?, ,\n"
            + $"Too long?,{new string('a', 4001)},\n"
            + "Gym?,No.,Missing.pdf\n";

        var error = await Should.ThrowAsync<ValidationException>(() => scope.Send(new ImportEvalQuestionsCommand(set.Id, csv)));

        error.Errors["Csv"].ShouldBe([
            "Row 3: the question must have 1 to 1000 characters.",
            "Row 4: the expected answer must have 1 to 4000 characters.",
            "Row 5: the expected answer must have 1 to 4000 characters.",
            "Row 6: the bot has no document named \"Missing.pdf\".",
        ]);
        (await scope.Send(new GetEvalSetQuery(set.Id))).Questions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Needs_the_columns_and_between_one_and_500_questions()
    {
        var seed = await SeedAsync(fixture, []);
        await using var scope = Scope(fixture, seed);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        var tooMany = "question,expected_answer\n" + string.Concat(Enumerable.Range(1, 501).Select(i => $"Q{i}?,A{i}.\n"));

        async Task<string> Error(string csv) =>
            (await Should.ThrowAsync<ValidationException>(() => scope.Send(new ImportEvalQuestionsCommand(set.Id, csv)))).Errors["Csv"][0];

        (await Error("q,a\nVacation?,25.")).ShouldBe("The first row must name the columns question and expected_answer.");
        (await Error("question,expected_answer\n\n")).ShouldBe("The file must have between 1 and 500 questions.");
        (await Error(tooMany)).ShouldBe("The file must have between 1 and 500 questions.");
        (await Error("")).ShouldBe("Choose a CSV file with questions.");
        (await Error(new string('x', 1_000_001))).ShouldBe("The file is too large (at most 1 MB).");
    }

    [Fact]
    public async Task Viewers_cannot_import()
    {
        var seed = await SeedAsync(fixture, []);
        var viewer = seed with { UserId = await CreateUserAsync(fixture, seed.Tenant.Id, UserRole.Viewer), Role = UserRole.Viewer };
        EvalSetDto set;
        await using (var scope = Scope(fixture, seed))
        {
            set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        }

        await using var asViewer = Scope(fixture, viewer);
        await Should.ThrowAsync<ForbiddenException>(
            () => asViewer.Send(new ImportEvalQuestionsCommand(set.Id, "question,expected_answer\nQ?,A.")));
    }
}
