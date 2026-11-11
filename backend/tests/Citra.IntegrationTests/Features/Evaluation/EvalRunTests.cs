using Citra.Application.Common.Chat;
using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Exceptions;
using Citra.Application.Features.Evaluation;
using Citra.Application.Features.Evaluation.Runs;
using Citra.Domain.Documents;
using Citra.Domain.Evaluation;
using Citra.Domain.Users;
using Citra.IntegrationTests.Features.Chat;
using Citra.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Citra.IntegrationTests.Features.Chat.ChatData;

namespace Citra.IntegrationTests.Features.Evaluation;

/// <summary>
/// C-242–C-245: an eval run is started as pending and the Worker answers every test question like a new chat would
/// (without saving conversations), grades it and saves the results.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class EvalRunTests(DatabaseFixture fixture)
{
    private const string VacationChunk = "Employees get 25 vacation days per year.";
    private static readonly ChatUsage Usage = new("claude-test", 10, 5); // $0.00007 with the test prices

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Answers_every_question_in_order_with_cost_and_saves_no_chats()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        await using var scope = Scope(fixture, seed);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        var vacation = await scope.Send(new AddEvalQuestionCommand(set.Id, "How many vacation days?", "25 days.", null));
        var parking = await scope.Send(new AddEvalQuestionCommand(set.Id, "Where do I park?", "Behind the office.", null));
        var asked = new List<string>();

        EvalRunDto run;
        using (fixture.Chat.Script(request =>
        {
            asked.Add(request.Messages[^1].Content);
            return FakeChatModel.Stream(Usage, asked.Count == 1 ? "You get 25 days [1]." : "[[NOT_FOUND]] Not in the documents.");
        }))
        {
            run = await StartAndRunAsync(seed, set.Id, " prompt v1 ");
        }

        (run.Status, run.TotalQuestions, run.Notes, run.BotId).ShouldBe((EvalRunStatus.Completed, 2, "prompt v1", seed.Bot.Id));
        run.Model.ShouldBe(fixture.Chat.DefaultModel);
        (run.CorrectCount, run.PartialCount, run.Score).ShouldBe((1, 0, 50));
        asked[0].ShouldContain("How many vacation days?");
        asked[0].ShouldContain(VacationChunk);
        asked[1].ShouldContain("Where do I park?");

        var results = await scope.Db.EvalResults.Where(r => r.EvalRunId == run.Id).ToListAsync(Ct);
        results.Select(r => (r.EvalQuestionId, r.ActualAnswer)).ShouldBe(
            [(vacation.Id, "You get 25 days [1]."), (parking.Id, "Not in the documents.")], ignoreOrder: true);
        results.ShouldAllBe(r => r.CostUsd >= 0.00007m && r.LatencyMs >= 0); // the tiny search cost rounds away (6 decimals)
        results.Single(r => r.EvalQuestionId == vacation.Id).Grade.ShouldBe(EvalGrade.Correct, "the word judge grades (no real model)");
        results.Single(r => r.EvalQuestionId == parking.Id).JudgeReason.ShouldBe("The bot did not find the answer in the documents.");
        run.TotalCostUsd.ShouldBe(results.Sum(r => r.CostUsd));

        (await scope.Db.Conversations.CountAsync(c => c.BotId == seed.Bot.Id, Ct)).ShouldBe(0);
        (await scope.Db.EmbeddingUsages.CountAsync(u => u.BotId == seed.Bot.Id, Ct)).ShouldBe(2, "each search was billed");
    }

    [Fact]
    public async Task Counts_a_retrieval_hit_when_the_expected_document_was_found()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        await using var scope = Scope(fixture, seed);
        var handbook = await scope.Db.Documents.Where(d => d.BotId == seed.Bot.Id).Select(d => d.Id).SingleAsync(Ct);
        var unsearched = Document.FromUpload(
            seed.Tenant.Id, seed.Bot.Id, "Policy.pdf", "application/pdf", 100, DocumentSourceType.Pdf,
            $"{seed.Tenant.Id}/{seed.Bot.Id}/policy.pdf", new string('b', 64)); // no chunks: the search never finds it
        scope.Db.Documents.Add(unsearched);
        await scope.Db.SaveChangesAsync(Ct);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        var found = await scope.Send(new AddEvalQuestionCommand(set.Id, "How many vacation days?", "25 days.", handbook));
        var missed = await scope.Send(new AddEvalQuestionCommand(set.Id, "How many vacation days again?", "25 days.", unsearched.Id));
        var noDocument = await scope.Send(new AddEvalQuestionCommand(set.Id, "Vacation days?", "25 days.", null));

        EvalRunDto run;
        using (fixture.Chat.Script(_ => FakeChatModel.Stream(Usage, "You get 25 days [1].")))
        {
            run = await StartAndRunAsync(seed, set.Id, null);
        }

        run.RetrievalHitCount.ShouldBe(1);
        var hits = await scope.Db.EvalResults.Where(r => r.EvalRunId == run.Id).ToDictionaryAsync(r => r.EvalQuestionId, r => r.RetrievalHit, Ct);
        hits.ShouldBe(new Dictionary<Guid, bool> { [found.Id] = true, [missed.Id] = false, [noDocument.Id] = false });
    }

    [Fact]
    public async Task A_failing_ai_service_fails_the_run_and_keeps_the_finished_answers()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        await using var scope = Scope(fixture, seed);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        await scope.Send(new AddEvalQuestionCommand(set.Id, "First?", "A.", null));
        await scope.Send(new AddEvalQuestionCommand(set.Id, "Second?", "B.", null));
        var calls = 0;

        EvalRunDto run;
        using (fixture.Chat.Script(_ => ++calls == 1
            ? FakeChatModel.Stream(Usage, "First answer.")
            : FakeChatModel.Failing(new ChatModelException("Overloaded"), "Part")))
        {
            run = await StartAndRunAsync(seed, set.Id, null);
        }

        (run.Status, run.Error, run.Score, run.AnsweredQuestions).ShouldBe(
            (EvalRunStatus.Failed, "The AI service failed: Overloaded", null, 1));
    }

    [Fact]
    public async Task An_unexpected_error_fails_the_run_and_keeps_the_finished_answers()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        await using var scope = Scope(fixture, seed);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        await scope.Send(new AddEvalQuestionCommand(set.Id, "First?", "A.", null));
        await scope.Send(new AddEvalQuestionCommand(set.Id, "Second?", "B.", null));
        var calls = 0;

        EvalRunDto run;
        using (fixture.Chat.Script(_ => ++calls == 1
            ? FakeChatModel.Stream(Usage, "First answer.")
            : FakeChatModel.Failing(new InvalidOperationException("Crash"), "Part")))
        {
            run = await StartAndRunAsync(seed, set.Id, null);
        }

        (run.Status, run.Error, run.AnsweredQuestions).ShouldBe((EvalRunStatus.Failed, "The run failed unexpectedly.", 1));
    }

    [Fact]
    public async Task Needs_questions_an_editor_and_a_short_note()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var viewer = seed with { UserId = await CreateUserAsync(fixture, seed.Tenant.Id, UserRole.Viewer), Role = UserRole.Viewer };
        EvalSetDto set;
        await using (var scope = Scope(fixture, seed))
        {
            set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
            var empty = await Should.ThrowAsync<ValidationException>(() => scope.Send(new StartEvalRunCommand(set.Id, null)));
            empty.Errors["EvalSetId"].ShouldBe(["Add questions to the set before running it."]);
            await scope.Send(new AddEvalQuestionCommand(set.Id, "Q?", "A.", null));
            var note = await Should.ThrowAsync<ValidationException>(
                () => scope.Send(new StartEvalRunCommand(set.Id, new string('n', 501))));
            note.Errors.Keys.ShouldBe(["Notes"]);
        }

        await using var asViewer = Scope(fixture, viewer);
        await Should.ThrowAsync<ForbiddenException>(() => asViewer.Send(new StartEvalRunCommand(set.Id, null)));
    }

    [Fact]
    public async Task A_refused_search_is_tried_again_and_a_lasting_one_fails_the_run()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        await using var scope = Scope(fixture, seed);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        await scope.Send(new AddEvalQuestionCommand(set.Id, "How many vacation days?", "25 days.", null));
        var searches = 0;
        var delay = EvalRunner.SearchRetryDelay;
        EvalRunner.SearchRetryDelay = TimeSpan.Zero;
        try
        {
            EvalRunDto recovered;
            using (fixture.Chat.Script(_ => FakeChatModel.Stream(Usage, "You get 25 days [1].")))
            using (fixture.Embeddings.Intercept((_, _) => ++searches < 3
                ? throw new EmbeddingException("Voyage AI rate limit reached (429).")
                : Task.CompletedTask))
            {
                recovered = await StartAndRunAsync(seed, set.Id, null);
            }

            (recovered.Status, recovered.AnsweredQuestions, searches).ShouldBe((EvalRunStatus.Completed, 1, 3));

            EvalRunDto failed;
            using (fixture.Embeddings.Intercept((_, _) => throw new EmbeddingException("Voyage AI rate limit reached (429).")))
            {
                failed = await StartAndRunAsync(seed, set.Id, null);
            }

            (failed.Status, failed.Error).ShouldBe((EvalRunStatus.Failed, "The AI service failed: Voyage AI rate limit reached (429)."));
        }
        finally
        {
            EvalRunner.SearchRetryDelay = delay;
        }
    }

    [Fact]
    public async Task A_run_starts_pending_and_only_its_company_can_see_it()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var stranger = await SeedAsync(fixture, []);
        await using var scope = Scope(fixture, seed);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        await scope.Send(new AddEvalQuestionCommand(set.Id, "Q?", "A.", null));

        var started = await scope.Send(new StartEvalRunCommand(set.Id, null));

        (started.Status, started.AnsweredQuestions, started.StartedAt).ShouldBe((EvalRunStatus.Pending, 0, null));
        (await scope.Send(new GetEvalRunQuery(started.Id))).Status.ShouldBe(EvalRunStatus.Pending);
        await using var asStranger = Scope(fixture, stranger);
        await Should.ThrowAsync<NotFoundException>(() => asStranger.Send(new GetEvalRunQuery(started.Id)));
        await RunPendingAsync();
        (await scope.Send(new GetEvalRunQuery(started.Id))).Status.ShouldBe(EvalRunStatus.Completed);
    }

    [Fact]
    public async Task Lists_a_sets_runs_and_a_runs_results_in_question_order()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        var stranger = await SeedAsync(fixture, []);
        await using var scope = Scope(fixture, seed);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        var first = await scope.Send(new AddEvalQuestionCommand(set.Id, "How many vacation days?", "25 days.", null));
        var second = await scope.Send(new AddEvalQuestionCommand(set.Id, "Where do I park?", "Behind the office.", null));

        EvalRunDto older, newer;
        using (fixture.Chat.Script(request => FakeChatModel.Stream(
            Usage, request.Messages[^1].Content.Contains("Where do I park?") ? "In front." : "You get 25 days [1].")))
        {
            older = await StartAndRunAsync(seed, set.Id, "v1");
            newer = await StartAndRunAsync(seed, set.Id, "v2");
        }

        var runs = await scope.Send(new GetEvalRunsQuery(set.Id));
        runs.Select(r => (r.Id, r.Notes, r.AnsweredQuestions)).ShouldBe([(newer.Id, "v2", 2), (older.Id, "v1", 2)]);

        var results = await scope.Send(new GetEvalRunResultsQuery(newer.Id));
        results.Select(r => (r.QuestionId, r.Question, r.ExpectedAnswer, r.ActualAnswer, r.Grade)).ShouldBe([
            (first.Id, "How many vacation days?", "25 days.", "You get 25 days [1].", EvalGrade.Correct),
            (second.Id, "Where do I park?", "Behind the office.", "In front.", EvalGrade.Wrong),
        ]);
        results[1].JudgeReason.ShouldBe("0% of the expected answer's words are in the answer (word comparison, no AI).");

        await using var asStranger = Scope(fixture, stranger);
        await Should.ThrowAsync<NotFoundException>(() => asStranger.Send(new GetEvalRunsQuery(set.Id)));
        await Should.ThrowAsync<NotFoundException>(() => asStranger.Send(new GetEvalRunResultsQuery(newer.Id)));
    }

    [Fact]
    public async Task The_worker_finds_nothing_when_no_run_is_pending()
    {
        await RunPendingAsync();
        await using var scope = fixture.OpenScope(tenantId: null);

        (await scope.Services.GetRequiredService<IEvalRunner>().RunNextAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_run_a_restart_interrupted_ends_as_failed_when_the_worker_starts_again()
    {
        var seed = await SeedAsync(fixture, [VacationChunk]);
        await using var scope = Scope(fixture, seed);
        var set = await scope.Send(new CreateEvalSetCommand(seed.Bot.Id, "HR", null));
        await scope.Send(new AddEvalQuestionCommand(set.Id, "Q?", "A.", null));
        var interrupted = await scope.Send(new StartEvalRunCommand(set.Id, null));
        var waiting = await scope.Send(new StartEvalRunCommand(set.Id, null));
        await using (var crashed = fixture.OpenScope(tenantId: null))
        {
            // The old Worker had claimed the first run when it stopped.
            var run = await crashed.Db.EvalRuns.IgnoreQueryFilters().SingleAsync(r => r.Id == interrupted.Id, Ct);
            run.Start(DateTimeOffset.UtcNow);
            await crashed.Db.SaveChangesAsync(Ct);
        }

        await using (var worker = fixture.OpenScope(tenantId: null))
        {
            (await worker.Services.GetRequiredService<IEvalRunner>().FailInterruptedAsync(Ct)).ShouldBe(1);
        }

        var ended = await scope.Send(new GetEvalRunQuery(interrupted.Id));
        (ended.Status, ended.Error).ShouldBe((EvalRunStatus.Failed, EvalRunner.InterruptedMessage));
        (await scope.Send(new GetEvalRunQuery(waiting.Id))).Status.ShouldBe(EvalRunStatus.Pending, "runs not started yet still run");
        await RunPendingAsync();
    }

    /// <summary>Starts a run as the seed's user, lets the Worker run every pending run, and reads the run back.</summary>
    private async Task<EvalRunDto> StartAndRunAsync(ChatSeed seed, Guid setId, string? notes)
    {
        await using var scope = Scope(fixture, seed);
        var started = await scope.Send(new StartEvalRunCommand(setId, notes));
        await RunPendingAsync();
        return await scope.Send(new GetEvalRunQuery(started.Id));
    }

    /// <summary>What the Worker does: runs pending runs without a user, until none is left.</summary>
    private async Task RunPendingAsync()
    {
        await using var worker = fixture.OpenScope(tenantId: null);
        var runner = worker.Services.GetRequiredService<IEvalRunner>();
        while (await runner.RunNextAsync(Ct))
        {
        }
    }
}
