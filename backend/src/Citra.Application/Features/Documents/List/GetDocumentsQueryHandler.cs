using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Paging;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Bots;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Documents.List;

internal sealed class GetDocumentsQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetDocumentsQuery, PagedResult<DocumentDto>>
{
    public async Task<PagedResult<DocumentDto>> Handle(GetDocumentsQuery request, CancellationToken cancellationToken)
    {
        // Every role may view documents; the tenant filter limits everything to the user's company.
        currentUser.RequireTenantId();

        // "No documents" and "no such bot" must look different to the UI.
        if (!await db.Bots.AnyAsync(b => b.Id == request.BotId, cancellationToken))
        {
            throw new NotFoundException(nameof(Bot), request.BotId);
        }

        var query = db.Documents.AsNoTracking().Where(d => d.BotId == request.BotId);

        if (request.Status is { } status)
        {
            var statuses = status.Statuses();
            query = query.Where(d => statuses.Contains(d.Status));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Case-insensitive "contains"; EF escapes % and _ in the term, so they match literally.
            var term = request.Search.Trim().ToLower();
            query = query.Where(d => d.FileName.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);

        // Newest first; the id breaks ties so paging is stable (no row shows up on two pages).
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<DocumentDto>(
            items.Select(DocumentDto.From).ToList(), request.Page, request.PageSize, total);
    }
}
