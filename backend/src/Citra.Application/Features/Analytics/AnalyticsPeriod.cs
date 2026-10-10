using Citra.Application.Common.Persistence;
using Citra.Domain.Conversations;
using FluentValidation;

namespace Citra.Application.Features.Analytics;

/// <summary>The days a dashboard query covers (UTC, both included), for one bot or, without a bot, all of them.</summary>
public interface IAnalyticsPeriod
{
    Guid? BotId { get; }

    DateOnly From { get; }

    DateOnly To { get; }
}

internal static class AnalyticsPeriod
{
    public const int MaxDays = 366;

    public static DateTimeOffset Start(this IAnalyticsPeriod period) =>
        new(period.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>The first moment after the period.</summary>
    public static DateTimeOffset End(this IAnalyticsPeriod period) =>
        new(period.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>
    /// The period's messages of the bot (or all bots). Messages are not tenant-owned; the tenant filter on
    /// conversations keeps them within the company.
    /// </summary>
    public static IQueryable<Message> Messages(this IApplicationDbContext db, IAnalyticsPeriod period)
    {
        var (start, end) = (period.Start(), period.End());
        var conversations = db.Conversations.Where(c => period.BotId == null || c.BotId == period.BotId).Select(c => c.Id);
        return db.Messages.Where(m => m.CreatedAt >= start && m.CreatedAt < end && conversations.Contains(m.ConversationId));
    }

    public static void AddPeriodRules<T>(this AbstractValidator<T> validator)
        where T : IAnalyticsPeriod
    {
        validator.RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From);
        validator.RuleFor(x => x.To)
            .Must((query, to) => to.DayNumber - query.From.DayNumber < MaxDays)
            .WithMessage($"Choose at most {MaxDays} days.");
    }
}
