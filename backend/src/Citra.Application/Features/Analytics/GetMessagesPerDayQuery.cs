using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Conversations;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Analytics;

/// <summary>Questions asked per day (UTC) for the dashboard chart: every day of the period, days without questions as 0.</summary>
public sealed record GetMessagesPerDayQuery(Guid? BotId, DateOnly From, DateOnly To)
    : IRequest<IReadOnlyList<DayCountDto>>, IAnalyticsPeriod;

public sealed record DayCountDto(DateOnly Date, int Questions);

internal sealed class GetMessagesPerDayQueryValidator : AbstractValidator<GetMessagesPerDayQuery>
{
    public GetMessagesPerDayQueryValidator() => this.AddPeriodRules();
}

internal sealed class GetMessagesPerDayQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMessagesPerDayQuery, IReadOnlyList<DayCountDto>>
{
    public async Task<IReadOnlyList<DayCountDto>> Handle(GetMessagesPerDayQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();

        var counts = await db.Messages(request)
            .Where(m => m.Role == MessageRole.User)
            .GroupBy(m => m.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => DateOnly.FromDateTime(x.Day), x => x.Count, cancellationToken);

        var days = request.To.DayNumber - request.From.DayNumber + 1;
        return [.. Enumerable.Range(0, days)
            .Select(i => request.From.AddDays(i))
            .Select(day => new DayCountDto(day, counts.GetValueOrDefault(day)))];
    }
}
