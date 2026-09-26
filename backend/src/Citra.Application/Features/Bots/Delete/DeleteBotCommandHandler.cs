using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Application.Common.Storage;
using Citra.Domain.Bots;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Citra.Application.Features.Bots.Delete;

internal sealed class DeleteBotCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    ILogger<DeleteBotCommandHandler> logger) : IRequestHandler<DeleteBotCommand>
{
    public async Task Handle(DeleteBotCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireEditorTenantId();

        var bot = await db.Bots.SingleOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Bot), request.Id);

        // One DELETE; the database's ON DELETE CASCADE rules remove documents, chunks, jobs,
        // conversations, messages and evals in the same transaction.
        db.Bots.Remove(bot);
        await db.SaveChangesAsync(cancellationToken);

        // Files go only after the database commit: a failed commit must not leave documents without files.
        // Not cancellable (the bot is already gone), and a failure only leaves orphaned files behind, so log it.
        try
        {
            await storage.DeleteFolderAsync(StoragePaths.BotFolder(bot.TenantId, bot.Id), CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Bot {BotId} was deleted, but its files could not be removed", bot.Id);
        }
    }
}
