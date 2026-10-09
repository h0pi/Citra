using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Bots;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Bots.GetById;

internal sealed class GetBotByIdQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetBotByIdQuery, BotDto>
{
    public async Task<BotDto> Handle(GetBotByIdQuery request, CancellationToken cancellationToken)
    {
        // Every role may view a bot's settings (only Owner/Admin may change them).
        currentUser.RequireTenantId();

        // Another company's bot is filtered out by the tenant filter, so it is reported as not found.
        var bot = await db.Bots
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Bot), request.Id);

        return await BotDto.FromAsync(bot, db, cancellationToken);
    }
}
