using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Conversations;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Chat.Conversations;

/// <summary>One of the current user's conversations with all its messages and their sources (to reopen a chat).</summary>
public sealed record GetConversationQuery(Guid Id) : IRequest<ConversationDto>;

internal sealed class GetConversationQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetConversationQuery, ConversationDto>
{
    public async Task<ConversationDto> Handle(GetConversationQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();
        var userId = currentUser.RequireUserId();

        var conversation = await db.Own(userId).AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Conversation), request.Id);

        // A question and its answer are saved separately, the question first; the role orders exact ties.
        var messages = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .Include(m => m.Citations)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Role == MessageRole.User ? 0 : 1)
            .ToListAsync(cancellationToken);

        return new ConversationDto(
            conversation.Id,
            conversation.BotId,
            conversation.Title,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            [.. messages.Select(m => new ConversationMessageDto(
                m.Id,
                m.Role,
                m.Content,
                m.CreatedAt,
                m.WasAnswered,
                m.Feedback,
                [.. m.Citations.OrderBy(c => c.Number).Select(c => new CitationDto(
                    c.Number, c.ChunkId, c.DocumentId, c.DocumentName, c.PageNumber, c.Snippet, c.Score))]))]);
    }
}
