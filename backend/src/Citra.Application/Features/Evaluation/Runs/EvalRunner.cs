using Citra.Application.Common.Chat;
using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Persistence;
using Citra.Domain.Bots;
using Citra.Domain.Evaluation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Citra.Application.Features.Evaluation.Runs;

/// <summary>Runs eval runs in the background: the Worker's loop calls <see cref="RunNextAsync"/> in a fresh DI scope.</summary>
public interface IEvalRunner
{
    /// <summary>Runs the oldest pending eval run, if any. True when one was found (check again right away).</summary>
    Task<bool> RunNextAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ends runs a stopped Worker left "Running" (a restart or deploy in the middle of a run): nobody would ever
    /// finish them and their page would wait forever. Called once when the Worker starts; Citra runs one Worker.
    /// Returns how many runs were ended.
    /// </summary>
    Task<int> FailInterruptedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Runs an eval: every test question of the set is answered by the bot as a new chat would answer it, and each
/// result is graded and saved with its cost and time. No conversations are saved. There is no user in the
/// background, so the company filter is lifted: the run names its company and bot.
/// </summary>
internal sealed class EvalRunner(
    IApplicationDbContext db,
    EvalAnswerer answerer,
    IAnswerJudge judge,
    TimeProvider timeProvider,
    ILogger<EvalRunner> logger) : IEvalRunner
{
    /// <summary>
    /// Voyage's free tier allows 3 searches a minute, fewer than a run needs: a refused search waits this long and is
    /// tried again, a few times. (Tests shorten it.)
    /// </summary>
    internal static TimeSpan SearchRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    internal const int SearchAttempts = 4;

    public async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        var runs = db.EvalRuns.IgnoreQueryFilters([QueryFilters.Tenant]);
        var next = await runs.Where(r => r.Status == EvalRunStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (next is not { } id)
        {
            return false;
        }

        // Claimed in one statement, so two Workers never run the same eval.
        var now = timeProvider.GetUtcNow();
        var claimed = await runs.Where(r => r.Id == id && r.Status == EvalRunStatus.Pending)
            .ExecuteUpdateAsync(
                r => r.SetProperty(x => x.Status, EvalRunStatus.Running).SetProperty(x => x.StartedAt, now), cancellationToken);
        if (claimed == 1)
        {
            await RunAsync(await runs.SingleAsync(r => r.Id == id, cancellationToken), cancellationToken);
        }

        return true;
    }

    internal const string InterruptedMessage = "The run was interrupted because the server restarted. Start it again.";

    public async Task<int> FailInterruptedAsync(CancellationToken cancellationToken)
    {
        var interrupted = await db.EvalRuns.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(r => r.Status == EvalRunStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (var run in interrupted)
        {
            logger.LogWarning("Eval run {EvalRunId} was interrupted by a restart; marking it failed", run.Id);
            run.Fail(InterruptedMessage, timeProvider.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        return interrupted.Count;
    }

    private async Task RunAsync(EvalRun run, CancellationToken cancellationToken)
    {
        logger.LogInformation("Running eval run {EvalRunId} ({Questions} questions)", run.Id, run.TotalQuestions);
        var questions = await db.EvalQuestions.AsNoTracking()
            .Where(q => q.EvalSetId == run.EvalSetId)
            .OrderBy(q => q.Position)
            .ToListAsync(cancellationToken);
        var bot = await db.Bots.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .SingleAsync(b => b.Id == run.BotId, cancellationToken);

        try
        {
            foreach (var question in questions)
            {
                var answer = await AnswerAsync(bot, question.Question, cancellationToken);

                // Retrieval is scored only for questions that name the document the answer should come from.
                var retrievalHit = question.ExpectedDocumentId is { } expected && answer.SourceDocumentIds.Contains(expected);

                // "Not found" is wrong whatever its words: the documents should have had the answer.
                var verdict = answer.WasAnswered
                    ? await judge.GradeAsync(question.Question, question.ExpectedAnswer, answer.Answer, cancellationToken)
                    : new JudgeVerdict(EvalGrade.Wrong, "The bot did not find the answer in the documents.", 0);
                run.AddResult(
                    question.Id, answer.Answer, verdict.Grade, verdict.Reason, retrievalHit, answer.LatencyMs,
                    answer.CostUsd + verdict.CostUsd);

                // Saved one by one: results already paid for stay, and the page can show the progress.
                await db.SaveChangesAsync(cancellationToken);
            }

            run.Complete(timeProvider.GetUtcNow());
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // In the background nobody would see an exception: the run shows that it failed, and why.
            logger.LogWarning(ex, "Eval run {EvalRunId} failed", run.Id);
            run.Fail(
                ex is ChatModelException or EmbeddingException ? $"The AI service failed: {ex.Message}" : "The run failed unexpectedly.",
                timeProvider.GetUtcNow());
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<EvalAnswer> AnswerAsync(Bot bot, string question, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await answerer.AnswerAsync(bot, question, cancellationToken);
            }
            catch (EmbeddingException ex) when (attempt < SearchAttempts)
            {
                logger.LogInformation(ex, "Search refused (attempt {Attempt}); trying again in {Delay}", attempt, SearchRetryDelay);
                await Task.Delay(SearchRetryDelay, timeProvider, cancellationToken);
            }
        }
    }
}
