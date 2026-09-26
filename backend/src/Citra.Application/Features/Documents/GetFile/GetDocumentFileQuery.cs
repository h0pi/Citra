using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Application.Common.Storage;
using Citra.Domain.Documents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Documents.GetFile;

/// <summary>The original uploaded file, e.g. for the PDF viewer that highlights cited passages.</summary>
public sealed record GetDocumentFileQuery(Guid Id) : IRequest<DocumentFile>;

/// <summary>An open file: the caller must dispose <see cref="Content"/> (ASP.NET's File() result does).</summary>
public sealed record DocumentFile(Stream Content, string ContentType, string FileName);

internal sealed class GetDocumentFileQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage) : IRequestHandler<GetDocumentFileQuery, DocumentFile>
{
    public async Task<DocumentFile> Handle(GetDocumentFileQuery request, CancellationToken cancellationToken)
    {
        // Every role may open documents, but only their own company's (tenant filter → 404).
        currentUser.RequireTenantId();

        var document = await db.Documents
            .AsNoTracking()
            .Where(d => d.Id == request.Id)
            .Select(d => new { d.StoragePath, d.ContentType, d.FileName, d.SourceType })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Document), request.Id);

        if (document.StoragePath is null)
        {
            throw new NotFoundException(document.SourceType == DocumentSourceType.Url
                ? "Web pages have no stored file; open the page's address instead."
                : "This document has no stored file.");
        }

        // The row exists but the file does not (deleted by hand, lost disk): say so instead of a server error.
        var content = await storage.OpenReadAsync(document.StoragePath, cancellationToken)
            ?? throw new NotFoundException("The file of this document is missing. Upload it again.");

        return new DocumentFile(content, document.ContentType, document.FileName);
    }
}
