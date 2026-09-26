using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Bots.List;

internal sealed class GetBotsQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetBotsQuery, IReadOnlyList<BotSummaryDto>>
{
    public async Task<IReadOnlyList<BotSummaryDto>> Handle(GetBotsQuery request, CancellationToken cancellationToken)
    {
        // Every role may view bots; the tenant filter limits the list to the user's own company.
        currentUser.RequireTenantId();

        // One SQL query: the counts become correlated subqueries, served by the bot_id indexes.
        // No paging: a company has a handful of bots, not thousands.
        return await db.Bots
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .ThenBy(b => b.CreatedAt)
            .Select(b => new BotSummaryDto(
                b.Id,
                b.Name,
                b.Slug,
                b.Description,
                b.IsPublic,
                b.WidgetColor,
                db.Documents.Count(d => d.BotId == b.Id),
                db.Conversations.Where(c => c.BotId == b.Id).SelectMany(c => c.Messages).Count(),
                b.CreatedAt,
                b.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
