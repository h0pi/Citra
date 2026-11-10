using Citra.Application.Common.Chat;
using Citra.Application.Common.Persistence;
using Citra.Domain.Evaluation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Citra.Application.Features.Evaluation.Runs;

/// <summary>Runs eval runs in the background: the Worker's loop calls <see cref="RunNextAsync"/> in a fresh DI scope.</summary>
public interface IEvalRunner
{
    /// <summary>Runs the oldest pending eval run, if any. True when one was found (check again right away).</summary>
    Task<bool> RunNextAsync(CancellationToken cancellationToken);
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
                var answer = await answerer.AnswerAsync(bot, question.Question, cancellationToken);

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
                ex is ChatModelException ? $"The AI service failed: {ex.Message}" : "The run failed unexpectedly.",
                timeProvider.GetUtcNow());
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }
}
