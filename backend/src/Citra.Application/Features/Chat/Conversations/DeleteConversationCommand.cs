using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Conversations;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Chat.Conversations;

/// <summary>Deletes one of the current user's conversations with its messages, sources and ratings.</summary>
public sealed record DeleteConversationCommand(Guid Id) : IRequest;

internal sealed class DeleteConversationCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<DeleteConversationCommand>
{
    public async Task Handle(DeleteConversationCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();
        var userId = currentUser.RequireUserId();

        var conversation = await db.Own(userId).SingleOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Conversation), request.Id);

        // One DELETE: the database removes the messages and their citations with it (cascade).
        db.Conversations.Remove(conversation);
        await db.SaveChangesAsync(cancellationToken);
    }
}
