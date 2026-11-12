using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Analytics;

/// <summary>
/// The documents the period's answers cited most, counted per answer (an answer citing a document twice counts once).
/// Citations of deleted documents are left out.
/// </summary>
public sealed record GetMostCitedDocumentsQuery(Guid? BotId, DateOnly From, DateOnly To, int Limit = GetMostCitedDocumentsQuery.DefaultLimit)
    : IRequest<IReadOnlyList<CitedDocumentDto>>, IAnalyticsPeriod
{
    public const int DefaultLimit = 5;
    public const int MaxLimit = 50;
}

public sealed record CitedDocumentDto(Guid DocumentId, string Name, int Answers);

internal sealed class GetMostCitedDocumentsQueryValidator : AbstractValidator<GetMostCitedDocumentsQuery>
{
    public GetMostCitedDocumentsQueryValidator()
    {
        this.AddPeriodRules();
        RuleFor(x => x.Limit).InclusiveBetween(1, GetMostCitedDocumentsQuery.MaxLimit);
    }
}

internal sealed class GetMostCitedDocumentsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMostCitedDocumentsQuery, IReadOnlyList<CitedDocumentDto>>
{
    public async Task<IReadOnlyList<CitedDocumentDto>> Handle(GetMostCitedDocumentsQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();

        // The period's messages are limited to the company (tenant filter on conversations); citations follow them.
        var answers = db.Messages(request).Select(m => m.Id);
        var documents = await db.MessageCitations
            .Where(c => c.DocumentId != null && answers.Contains(c.MessageId))
            .GroupBy(c => c.DocumentId!.Value)
            .Select(g => new
            {
                DocumentId = g.Key,
                Name = g.Max(c => c.DocumentName)!,
                Answers = g.Select(c => c.MessageId).Distinct().Count(),
            })
            .OrderByDescending(d => d.Answers)
            .ThenBy(d => d.Name)
            .Take(request.Limit)
            .ToListAsync(cancellationToken);

        return [.. documents.Select(d => new CitedDocumentDto(d.DocumentId, d.Name, d.Answers))];
    }
}
