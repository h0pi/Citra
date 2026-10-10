using System.Text.RegularExpressions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Conversations;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Analytics;

/// <summary>
/// The most asked questions of the period. Questions that differ only in case, punctuation or spacing count as one
/// ("How many vacation days?" = "how many vacation days"); the most common wording is shown.
/// </summary>
public sealed record GetTopQuestionsQuery(Guid? BotId, DateOnly From, DateOnly To, int Limit = GetTopQuestionsQuery.DefaultLimit)
    : IRequest<IReadOnlyList<TopQuestionDto>>, IAnalyticsPeriod
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;
}

public sealed record TopQuestionDto(string Question, int Count);

internal sealed class GetTopQuestionsQueryValidator : AbstractValidator<GetTopQuestionsQuery>
{
    public GetTopQuestionsQueryValidator()
    {
        this.AddPeriodRules();
        RuleFor(x => x.Limit).InclusiveBetween(1, GetTopQuestionsQuery.MaxLimit);
    }
}

internal sealed partial class GetTopQuestionsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetTopQuestionsQuery, IReadOnlyList<TopQuestionDto>>
{
    public async Task<IReadOnlyList<TopQuestionDto>> Handle(GetTopQuestionsQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();

        // Exact wordings are counted in the database; the finer normalization (any language's punctuation) runs here.
        var wordings = await db.Messages(request)
            .Where(m => m.Role == MessageRole.User)
            .GroupBy(m => m.Content)
            .Select(g => new { Text = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return [.. wordings
            .GroupBy(w => Normalize(w.Text))
            .Where(g => g.Key.Length > 0)
            .Select(g => new TopQuestionDto(
                g.OrderByDescending(w => w.Count).ThenBy(w => w.Text, StringComparer.Ordinal).First().Text.Trim(),
                g.Sum(w => w.Count)))
            .OrderByDescending(q => q.Count)
            .ThenBy(q => q.Question, StringComparer.Ordinal)
            .Take(request.Limit)];
    }

    private static string Normalize(string question) =>
        Spaces().Replace(Punctuation().Replace(question.ToLowerInvariant(), " "), " ").Trim();

    [GeneratedRegex(@"[\p{P}\p{S}]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
