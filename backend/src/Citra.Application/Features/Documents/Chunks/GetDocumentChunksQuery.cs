using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Paging;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Documents;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Documents.Chunks;

/// <summary>
/// A document's chunks in reading order, one page at a time: what the bot actually searches, so people can check
/// how their document was split (and later, which passage an answer cites).
/// </summary>
public sealed record GetDocumentChunksQuery(
    Guid DocumentId,
    int Page = 1,
    int PageSize = GetDocumentChunksQuery.DefaultPageSize) : IRequest<PagedResult<ChunkDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

/// <summary>One chunk as the document's detail view shows it (without its embedding vector).</summary>
/// <param name="Index">Position in the document: 0, 1, 2, …</param>
/// <param name="PageNumber">1-based page for PDF/Word; null for text files and web pages.</param>
/// <param name="CharStart">Where <paramref name="Content"/> starts in its page's text (for highlighting later).</param>
public sealed record ChunkDto(
    Guid Id,
    int Index,
    int? PageNumber,
    string? Heading,
    string Content,
    int TokenCount,
    int CharStart,
    int CharEnd);

internal sealed class GetDocumentChunksQueryValidator : AbstractValidator<GetDocumentChunksQuery>
{
    public GetDocumentChunksQueryValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetDocumentChunksQuery.MaxPageSize);
    }
}

internal sealed class GetDocumentChunksQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetDocumentChunksQuery, PagedResult<ChunkDto>>
{
    public async Task<PagedResult<ChunkDto>> Handle(GetDocumentChunksQuery request, CancellationToken cancellationToken)
    {
        // Every role may view. Chunks carry no company of their own: access is decided by their document,
        // which the tenant filter hides when it belongs to another company (404).
        currentUser.RequireTenantId();

        if (!await db.Documents.AnyAsync(d => d.Id == request.DocumentId, cancellationToken))
        {
            throw new NotFoundException(nameof(Document), request.DocumentId);
        }

        var chunks = db.Chunks.AsNoTracking().Where(c => c.DocumentId == request.DocumentId);
        var total = await chunks.CountAsync(cancellationToken);
        var items = await chunks
            .OrderBy(c => c.Index)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new ChunkDto(
                c.Id, c.Index, c.PageNumber, c.Heading, c.Content, c.TokenCount, c.CharStart, c.CharEnd))
            .ToListAsync(cancellationToken);

        return new PagedResult<ChunkDto>(items, request.Page, request.PageSize, total);
    }
}
