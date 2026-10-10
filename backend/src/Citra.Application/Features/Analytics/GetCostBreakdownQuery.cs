using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Analytics;

/// <summary>
/// What the period's AI use cost per bot: answers (chat model) and embeddings (documents and questions), the most
/// expensive bot first. Every bot is listed, also without cost.
/// </summary>
public sealed record GetCostBreakdownQuery(Guid? BotId, DateOnly From, DateOnly To)
    : IRequest<IReadOnlyList<BotCostDto>>, IAnalyticsPeriod;

public sealed record BotCostDto(Guid BotId, string BotName, decimal ChatCostUsd, decimal EmbeddingCostUsd);

internal sealed class GetCostBreakdownQueryValidator : AbstractValidator<GetCostBreakdownQuery>
{
    public GetCostBreakdownQueryValidator() => this.AddPeriodRules();
}

internal sealed class GetCostBreakdownQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCostBreakdownQuery, IReadOnlyList<BotCostDto>>
{
    public async Task<IReadOnlyList<BotCostDto>> Handle(GetCostBreakdownQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();
        var (start, end) = (request.Start(), request.End());

        // Only answers carry a cost (questions record none).
        var chat = await db.Messages(request)
            .Join(db.Conversations, m => m.ConversationId, c => c.Id, (m, c) => new { c.BotId, m.CostUsd })
            .GroupBy(x => x.BotId)
            .Select(g => new { BotId = g.Key, Cost = g.Sum(x => x.CostUsd) })
            .ToDictionaryAsync(x => x.BotId, x => x.Cost, cancellationToken);

        var embeddings = await db.EmbeddingUsages
            .Where(u => u.BotId != null && u.CreatedAt >= start && u.CreatedAt < end)
            .GroupBy(u => u.BotId!.Value)
            .Select(g => new { BotId = g.Key, Cost = g.Sum(u => u.CostUsd) })
            .ToDictionaryAsync(x => x.BotId, x => x.Cost, cancellationToken);

        var bots = await db.Bots
            .Where(b => request.BotId == null || b.Id == request.BotId)
            .Select(b => new { b.Id, b.Name })
            .ToListAsync(cancellationToken);

        return [.. bots
            .Select(b => new BotCostDto(b.Id, b.Name, chat.GetValueOrDefault(b.Id), embeddings.GetValueOrDefault(b.Id)))
            .OrderByDescending(c => c.ChatCostUsd + c.EmbeddingCostUsd)
            .ThenBy(c => c.BotName, StringComparer.Ordinal)];
    }
}
