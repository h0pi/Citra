using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Bots;
using Citra.Domain.Evaluation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Evaluation;

/// <summary>One row of a bot's eval set list.</summary>
public sealed record EvalSetSummaryDto(Guid Id, Guid BotId, string Name, string? Description, int QuestionCount, DateTimeOffset CreatedAt);

/// <summary>An eval set with its questions, in the order they were added.</summary>
public sealed record EvalSetDto(Guid Id, Guid BotId, string Name, string? Description, IReadOnlyList<EvalQuestionDto> Questions);

/// <param name="ExpectedDocumentId">The document the answer should come from (optional; scores retrieval).</param>
public sealed record EvalQuestionDto(Guid Id, string Question, string ExpectedAnswer, Guid? ExpectedDocumentId);

/// <summary>A bot's eval sets (test questions with expected answers), newest first.</summary>
public sealed record GetEvalSetsQuery(Guid BotId) : IRequest<IReadOnlyList<EvalSetSummaryDto>>;

/// <summary>One eval set with all its questions.</summary>
public sealed record GetEvalSetQuery(Guid Id) : IRequest<EvalSetDto>;

internal sealed class GetEvalSetsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetEvalSetsQuery, IReadOnlyList<EvalSetSummaryDto>>
{
    public async Task<IReadOnlyList<EvalSetSummaryDto>> Handle(GetEvalSetsQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();
        if (!await db.Bots.AnyAsync(b => b.Id == request.BotId, cancellationToken))
        {
            throw new NotFoundException(nameof(Bot), request.BotId);
        }

        return await db.EvalSets.AsNoTracking()
            .Where(s => s.BotId == request.BotId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new EvalSetSummaryDto(s.Id, s.BotId, s.Name, s.Description, s.Questions.Count, s.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}

internal sealed class GetEvalSetQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetEvalSetQuery, EvalSetDto>
{
    public async Task<EvalSetDto> Handle(GetEvalSetQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();
        return EvalSets.ToDto(await db.FindEvalSetAsync(request.Id, cancellationToken));
    }
}

internal static class EvalSets
{
    /// <summary>The company's eval set with its questions (the tenant filter hides other companies' sets: 404).</summary>
    public static async Task<EvalSet> FindEvalSetAsync(this IApplicationDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.EvalSets.Include(s => s.Questions).SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
        ?? throw new NotFoundException(nameof(EvalSet), id);

    public static EvalSetDto ToDto(EvalSet set) => new(
        set.Id,
        set.BotId,
        set.Name,
        set.Description,
        [.. set.Questions.OrderBy(q => q.Position).Select(ToDto)]);

    public static EvalQuestionDto ToDto(EvalQuestion question) =>
        new(question.Id, question.Question, question.ExpectedAnswer, question.ExpectedDocumentId);
}
