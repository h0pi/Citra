using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Bots;
using MediatR;

namespace Citra.Application.Features.Bots.Create;

internal sealed class CreateBotCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<CreateBotCommand, BotDto>
{
    public async Task<BotDto> Handle(CreateBotCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.RequireEditorTenantId();

        var bot = Bot.Create(tenantId, request.Name, request.Description);

        // The tenant filter limits this to the current company: two companies may both have a "support" bot.
        bot.ChangeSlug(await UniqueSlug.FindAsync(db.Bots.Select(b => b.Slug), bot.Slug, cancellationToken));

        db.Bots.Add(bot);
        await db.SaveChangesAsync(cancellationToken);

        return BotDto.From(bot);
    }
}
