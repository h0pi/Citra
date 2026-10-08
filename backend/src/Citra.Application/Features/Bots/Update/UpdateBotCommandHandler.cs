using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Bots;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Bots.Update;

internal sealed class UpdateBotCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<UpdateBotCommand, BotDto>
{
    public async Task<BotDto> Handle(UpdateBotCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireEditorTenantId();

        // Another company's bot is invisible through the tenant filter, so it is a 404, not a 403:
        // we do not even confirm that it exists.
        var bot = await db.Bots.SingleOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Bot), request.Id);

        bot.UpdateDetails(request.Name, request.Description, request.SystemPrompt, request.WelcomeMessage);
        bot.ConfigureModel(request.Model, request.Temperature);
        bot.ChangeWidgetColor(request.WidgetColor);
        bot.ChangeStarterQuestions(request.StarterQuestions);

        await db.SaveChangesAsync(cancellationToken);

        return BotDto.From(bot);
    }
}
