using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Documents.Reprocess;

/// <summary>
/// Sends a finished document (Ready or Failed) through processing again: after a failure was fixed, or when
/// extraction, chunking or the embedding model improved. Its old chunks are removed; the Worker makes new ones.
/// </summary>
public sealed record ReprocessDocumentCommand(Guid Id) : IRequest<DocumentDto>;

internal sealed class ReprocessDocumentCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<ReprocessDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> Handle(ReprocessDocumentCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireEditorTenantId();

        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), request.Id);

        if (!document.CanBeReprocessed)
        {
            throw new ConflictException(
                "This document is already queued or being processed. Try again when it has finished.");
        }

        document.ResetForReprocessing();
        db.ProcessingJobs.Add(ProcessingJob.For(document.Id));

        // One transaction: the Worker must never see the new job while the old chunks still exist,
        // or it could add fresh chunks that this delete then removes.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Chunks.Where(c => c.DocumentId == document.Id).ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return DocumentDto.From(document);
    }
}
