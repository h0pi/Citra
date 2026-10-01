using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Application.Features.Chat.Conversations;
using Citra.Domain.Conversations;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = Citra.Application.Common.Exceptions.ValidationException;

namespace Citra.Application.Features.Chat.Feedback;

/// <summary>
/// Thumbs up or down on an answer in the current user's own chat (None takes the rating back). Ratings show
/// which answers to improve, e.g. by adding a missing document.
/// </summary>
public sealed record RateAnswerCommand(Guid MessageId, MessageFeedback Feedback) : IRequest;

internal sealed class RateAnswerCommandValidator : AbstractValidator<RateAnswerCommand>
{
    public RateAnswerCommandValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty();
        RuleFor(x => x.Feedback).IsInEnum();
    }
}

internal sealed class RateAnswerCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<RateAnswerCommand>
{
    public async Task Handle(RateAnswerCommand request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();
        var userId = currentUser.RequireUserId();

        var own = db.Own(userId);
        var message = await db.Messages.SingleOrDefaultAsync(
                m => m.Id == request.MessageId && own.Any(c => c.Id == m.ConversationId), cancellationToken)
            ?? throw new NotFoundException(nameof(Message), request.MessageId);

        if (message.Role != MessageRole.Assistant)
        {
            throw new ValidationException(nameof(RateAnswerCommand.MessageId), "Only answers can be rated, not questions.");
        }

        message.GiveFeedback(request.Feedback);
        await db.SaveChangesAsync(cancellationToken);
    }
}
