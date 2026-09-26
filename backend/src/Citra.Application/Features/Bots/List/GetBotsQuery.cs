using MediatR;

namespace Citra.Application.Features.Bots.List;

/// <summary>All bots of the current user's company, for the bots page and the "current bot" selector.</summary>
public sealed record GetBotsQuery : IRequest<IReadOnlyList<BotSummaryDto>>;

/// <summary>One row of the bots list: enough to show a card, without the long prompt texts.</summary>
public sealed record BotSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    bool IsPublic,
    string WidgetColor,
    int DocumentCount,
    int MessageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
