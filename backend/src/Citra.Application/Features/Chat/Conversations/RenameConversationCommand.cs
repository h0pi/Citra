using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Conversations;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Chat.Conversations;

/// <summary>Gives one of the current user's conversations a new name (trimmed).</summary>
public sealed record RenameConversationCommand(Guid Id, string Title) : IRequest;

internal sealed class RenameConversationCommandValidator : AbstractValidator<RenameConversationCommand>
{
    public RenameConversationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Conversation.TitleMaxLength);
    }
}

internal sealed class RenameConversationCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<RenameConversationCommand>
{
    public async Task Handle(RenameConversationCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();
        var userId = currentUser.RequireUserId();

        var conversation = await db.Own(userId).SingleOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Conversation), request.Id);

        conversation.Rename(request.Title);
        await db.SaveChangesAsync(cancellationToken);
    }
}
