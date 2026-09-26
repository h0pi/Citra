using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Documents.GetById;

/// <summary>One document with its processing details (page and chunk counts, or why processing failed).</summary>
public sealed record GetDocumentByIdQuery(Guid Id) : IRequest<DocumentDto>;

internal sealed class GetDocumentByIdQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetDocumentByIdQuery, DocumentDto>
{
    public async Task<DocumentDto> Handle(GetDocumentByIdQuery request, CancellationToken cancellationToken)
    {
        // Every role may view; another company's document is hidden by the tenant filter (404).
        currentUser.RequireTenantId();

        var document = await db.Documents
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Document), request.Id);

        return DocumentDto.From(document);
    }
}
