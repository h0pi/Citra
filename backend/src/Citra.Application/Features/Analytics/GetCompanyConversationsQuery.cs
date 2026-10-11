using Citra.Application.Common.Paging;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Conversations;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Analytics;

/// <summary>
/// All chats of the company, most recently active first, for one bot or all, optionally only admin-app or website
/// chats. Read-only browsing for owners and admins (chats can hold private questions).
/// </summary>
public sealed record GetCompanyConversationsQuery(
    Guid? BotId,
    ConversationChannel? Channel,
    int Page = 1,
    int PageSize = GetCompanyConversationsQuery.DefaultPageSize) : IRequest<PagedResult<CompanyConversationDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

public sealed record CompanyConversationDto(
    Guid Id, Guid BotId, string Title, ConversationChannel Channel, int MessageCount, DateTimeOffset UpdatedAt);

internal sealed class GetCompanyConversationsQueryValidator : AbstractValidator<GetCompanyConversationsQuery>
{
    public GetCompanyConversationsQueryValidator()
    {
        RuleFor(x => x.Channel).IsInEnum();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetCompanyConversationsQuery.MaxPageSize);
    }
}

internal sealed class GetCompanyConversationsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCompanyConversationsQuery, PagedResult<CompanyConversationDto>>
{
    public async Task<PagedResult<CompanyConversationDto>> Handle(
        GetCompanyConversationsQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireAdminTenantId();

        // The tenant filter keeps this to the company's chats.
        var conversations = db.Conversations.AsNoTracking()
            .Where(c => request.BotId == null || c.BotId == request.BotId)
            .Where(c => request.Channel == null || c.Channel == request.Channel);
        var total = await conversations.CountAsync(cancellationToken);
        var items = await conversations
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new CompanyConversationDto(c.Id, c.BotId, c.Title, c.Channel, c.Messages.Count, c.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<CompanyConversationDto>(items, request.Page, request.PageSize, total);
    }
}
