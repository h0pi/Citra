using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Paging;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Bots;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Chat.Conversations;

/// <summary>The current user's chats with a bot, most recently active first, one page at a time.</summary>
public sealed record GetConversationsQuery(Guid BotId, int Page = 1, int PageSize = GetConversationsQuery.DefaultPageSize)
    : IRequest<PagedResult<ConversationSummaryDto>>
{
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 100;
}

internal sealed class GetConversationsQueryValidator : AbstractValidator<GetConversationsQuery>
{
    public GetConversationsQueryValidator()
    {
        RuleFor(x => x.BotId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetConversationsQuery.MaxPageSize);
    }
}

internal sealed class GetConversationsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetConversationsQuery, PagedResult<ConversationSummaryDto>>
{
    public async Task<PagedResult<ConversationSummaryDto>> Handle(GetConversationsQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();
        var userId = currentUser.RequireUserId();

        if (!await db.Bots.AnyAsync(b => b.Id == request.BotId, cancellationToken))
        {
            throw new NotFoundException(nameof(Bot), request.BotId);
        }

        var conversations = db.Own(userId).AsNoTracking().Where(c => c.BotId == request.BotId);
        var total = await conversations.CountAsync(cancellationToken);
        var items = await conversations
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new ConversationSummaryDto(c.Id, c.BotId, c.Title, c.Messages.Count, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ConversationSummaryDto>(items, request.Page, request.PageSize, total);
    }
}
