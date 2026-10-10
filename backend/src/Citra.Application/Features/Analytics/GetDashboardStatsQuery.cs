using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Conversations;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Analytics;

/// <summary>
/// The dashboard's headline numbers for the days <paramref name="From"/> to <paramref name="To"/> (UTC, both included),
/// for one bot or, without <paramref name="BotId"/>, all of the company's bots. Admin and website chats both count.
/// </summary>
public sealed record GetDashboardStatsQuery(Guid? BotId, DateOnly From, DateOnly To)
    : IRequest<DashboardStatsDto>, IAnalyticsPeriod;

/// <param name="Conversations">Chats started in the period.</param>
/// <param name="Questions">Questions asked in the period.</param>
/// <param name="AnsweredPercent">Share of answers found in the documents (0 when there were none).</param>
/// <param name="AverageLatencyMs">Average time from question to complete answer.</param>
/// <param name="TotalCostUsd">AI cost: answers plus embeddings (documents and questions).</param>
public sealed record DashboardStatsDto(
    int Conversations,
    int Questions,
    double AnsweredPercent,
    int AverageLatencyMs,
    decimal TotalCostUsd);

internal sealed class GetDashboardStatsQueryValidator : AbstractValidator<GetDashboardStatsQuery>
{
    public GetDashboardStatsQueryValidator() => this.AddPeriodRules();
}

internal sealed class GetDashboardStatsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetDashboardStatsQuery, DashboardStatsDto>
{
    public async Task<DashboardStatsDto> Handle(GetDashboardStatsQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();
        var (start, end) = (request.Start(), request.End());

        // The tenant filter on conversations keeps every number within the company.
        var conversations = db.Conversations.Where(c => request.BotId == null || c.BotId == request.BotId);
        var messages = db.Messages(request);
        var answers = messages.Where(m => m.Role == MessageRole.Assistant);

        var started = await conversations.CountAsync(c => c.CreatedAt >= start && c.CreatedAt < end, cancellationToken);
        var questions = await messages.CountAsync(m => m.Role == MessageRole.User, cancellationToken);
        var answerCount = await answers.CountAsync(cancellationToken);
        var answered = await answers.CountAsync(m => m.WasAnswered, cancellationToken);
        var latency = await answers.AverageAsync(m => (double?)m.LatencyMs, cancellationToken) ?? 0;
        var chatCost = await answers.SumAsync(m => m.CostUsd, cancellationToken);
        var embeddingCost = await db.EmbeddingUsages
            .Where(u => u.CreatedAt >= start && u.CreatedAt < end && (request.BotId == null || u.BotId == request.BotId))
            .SumAsync(u => u.CostUsd, cancellationToken);

        return new DashboardStatsDto(
            started,
            questions,
            answerCount == 0 ? 0 : Math.Round(100.0 * answered / answerCount, 1),
            (int)Math.Round(latency),
            chatCost + embeddingCost);
    }
}
