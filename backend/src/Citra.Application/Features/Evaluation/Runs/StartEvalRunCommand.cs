using Citra.Application.Common.Chat;
using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Evaluation;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = Citra.Application.Common.Exceptions.ValidationException;

namespace Citra.Application.Features.Evaluation.Runs;

/// <summary>
/// Starts a run of an eval set against its bot (owners and admins): it is saved as pending and the Worker answers
/// the questions in the background. <paramref name="Notes"/> labels the run, e.g. "prompt v2".
/// </summary>
public sealed record StartEvalRunCommand(Guid EvalSetId, string? Notes) : IRequest<EvalRunDto>;

/// <summary>One run with its progress: answered questions so far, then scores and cost.</summary>
public sealed record GetEvalRunQuery(Guid Id) : IRequest<EvalRunDto>;

/// <summary>An eval set's runs, newest first (to compare them).</summary>
public sealed record GetEvalRunsQuery(Guid EvalSetId) : IRequest<IReadOnlyList<EvalRunDto>>;

/// <summary>A run's result per question, in the set's order: the bot's answer, its grade and why.</summary>
public sealed record GetEvalRunResultsQuery(Guid Id) : IRequest<IReadOnlyList<EvalResultDto>>;

/// <param name="RetrievalHit">Whether the expected document was among the sources found (false without one).</param>
public sealed record EvalResultDto(
    Guid QuestionId,
    string Question,
    string ExpectedAnswer,
    Guid? ExpectedDocumentId,
    string ActualAnswer,
    EvalGrade Grade,
    string JudgeReason,
    bool RetrievalHit,
    int LatencyMs,
    decimal CostUsd);

/// <summary>One run of an eval set: its state, progress, scores and cost.</summary>
/// <param name="AnsweredQuestions">Questions answered so far (the progress while it runs).</param>
/// <param name="Score">0–100 (correct = 1 point, partly correct = ½), once completed.</param>
public sealed record EvalRunDto(
    Guid Id,
    Guid EvalSetId,
    Guid BotId,
    EvalRunStatus Status,
    string Model,
    string? Notes,
    int TotalQuestions,
    int AnsweredQuestions,
    int CorrectCount,
    int PartialCount,
    int RetrievalHitCount,
    double? Score,
    decimal TotalCostUsd,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt)
{
    public static EvalRunDto From(EvalRun run, int answeredQuestions) => new(
        run.Id, run.EvalSetId, run.BotId, run.Status, run.Model, run.Notes, run.TotalQuestions, answeredQuestions,
        run.CorrectCount, run.PartialCount, run.RetrievalHitCount, run.Score, run.TotalCostUsd, run.Error, run.CreatedAt,
        run.StartedAt, run.CompletedAt);
}

internal sealed class StartEvalRunCommandValidator : AbstractValidator<StartEvalRunCommand>
{
    public StartEvalRunCommandValidator() => RuleFor(x => x.Notes).MaximumLength(EvalRun.NotesMaxLength);
}

internal sealed class EvalRunHandlers(IApplicationDbContext db, ICurrentUser currentUser, IChatModel model) :
    IRequestHandler<StartEvalRunCommand, EvalRunDto>,
    IRequestHandler<GetEvalRunQuery, EvalRunDto>,
    IRequestHandler<GetEvalRunsQuery, IReadOnlyList<EvalRunDto>>,
    IRequestHandler<GetEvalRunResultsQuery, IReadOnlyList<EvalResultDto>>
{
    public async Task<EvalRunDto> Handle(StartEvalRunCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.RequireEditorTenantId();
        var set = await db.FindEvalSetAsync(request.EvalSetId, cancellationToken);
        if (set.Questions.Count == 0)
        {
            throw new ValidationException("EvalSetId", "Add questions to the set before running it.");
        }

        var bot = await db.Bots.SingleAsync(b => b.Id == set.BotId, cancellationToken);
        var run = EvalRun.Create(
            tenantId, set.Id, set.BotId, EvalAnswerer.BotModel(bot) ?? model.DefaultModel, set.Questions.Count, request.Notes);
        db.EvalRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return EvalRunDto.From(run, answeredQuestions: 0);
    }

    public async Task<EvalRunDto> Handle(GetEvalRunQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();

        // The company filter hides other companies' runs: they are not found.
        var run = await db.EvalRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(EvalRun), request.Id);
        var answered = await db.EvalResults.CountAsync(r => r.EvalRunId == run.Id, cancellationToken);
        return EvalRunDto.From(run, answered);
    }

    public async Task<IReadOnlyList<EvalRunDto>> Handle(GetEvalRunsQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();
        var set = await db.FindEvalSetAsync(request.EvalSetId, cancellationToken);

        return await db.EvalRuns.AsNoTracking()
            .Where(r => r.EvalSetId == set.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new EvalRunDto(
                r.Id, r.EvalSetId, r.BotId, r.Status, r.Model, r.Notes, r.TotalQuestions,
                db.EvalResults.Count(x => x.EvalRunId == r.Id), r.CorrectCount, r.PartialCount, r.RetrievalHitCount,
                r.Score, r.TotalCostUsd, r.Error, r.CreatedAt, r.StartedAt, r.CompletedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EvalResultDto>> Handle(GetEvalRunResultsQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();
        if (!await db.EvalRuns.AnyAsync(r => r.Id == request.Id, cancellationToken))
        {
            throw new NotFoundException(nameof(EvalRun), request.Id);
        }

        return await db.EvalResults.AsNoTracking()
            .Where(r => r.EvalRunId == request.Id)
            .Join(db.EvalQuestions, r => r.EvalQuestionId, q => q.Id, (r, q) => new { r, q })
            .OrderBy(x => x.q.Position)
            .Select(x => new EvalResultDto(
                x.q.Id, x.q.Question, x.q.ExpectedAnswer, x.q.ExpectedDocumentId, x.r.ActualAnswer, x.r.Grade,
                x.r.JudgeReason, x.r.RetrievalHit, x.r.LatencyMs, x.r.CostUsd))
            .ToListAsync(cancellationToken);
    }
}
