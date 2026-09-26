using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Application.Common.Storage;
using Citra.Domain.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Citra.Application.Features.Documents.Delete;

/// <summary>
/// Removes a document from its bot: the record, its chunks (so answers stop using it), its processing job
/// and the stored file. Citations in past chats keep their text but no longer link to the document.
/// </summary>
public sealed record DeleteDocumentCommand(Guid Id) : IRequest;

internal sealed class DeleteDocumentCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    ILogger<DeleteDocumentCommandHandler> logger) : IRequestHandler<DeleteDocumentCommand>
{
    public async Task Handle(DeleteDocumentCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireEditorTenantId();

        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), request.Id);

        // One DELETE; the database's rules remove chunks and jobs and unlink citations in the same transaction.
        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);

        // Files only after the commit (see DeleteBotCommandHandler for why). Web pages may have no folder yet.
        try
        {
            await storage.DeleteFolderAsync(
                StoragePaths.DocumentFolder(document.TenantId, document.BotId, document.Id), CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Document {DocumentId} was deleted, but its files could not be removed", document.Id);
        }
    }
}
