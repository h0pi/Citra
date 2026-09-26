using Citra.Application.Common.Paging;
using MediatR;

namespace Citra.Application.Features.Documents.List;

/// <summary>A bot's documents, newest first, one page at a time, optionally filtered by status and name.</summary>
public sealed record GetDocumentsQuery(
    Guid BotId,
    int Page = 1,
    int PageSize = GetDocumentsQuery.DefaultPageSize,
    DocumentStatusFilter? Status = null,
    string? Search = null) : IRequest<PagedResult<DocumentDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int SearchMaxLength = 200;
}
