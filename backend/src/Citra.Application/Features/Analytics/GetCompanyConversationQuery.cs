using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Application.Features.Chat.Conversations;
using Citra.Domain.Conversations;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Analytics;

/// <summary>
/// Any chat of the company, read-only, with its messages and sources: admin-app chats of every user and website
/// chats. For reviewing answers from the dashboard; chats can hold private questions, so only owners and admins.
/// </summary>
public sealed record GetCompanyConversationQuery(Guid Id) : IRequest<ConversationDto>;

internal sealed class GetCompanyConversationQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCompanyConversationQuery, ConversationDto>
{
    public async Task<ConversationDto> Handle(GetCompanyConversationQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireAdminTenantId();

        // The tenant filter hides other companies' chats: they are not found.
        var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Conversation), request.Id);

        return await ConversationTranscript.LoadAsync(db, conversation, cancellationToken);
    }
}
