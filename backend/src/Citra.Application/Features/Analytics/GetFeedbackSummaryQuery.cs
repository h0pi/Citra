using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Conversations;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Analytics;

/// <summary>
/// How people rated the period's answers: good and bad counts, and the badly rated answers (newest first, each with
/// its question and chat) to see what went wrong.
/// </summary>
public sealed record GetFeedbackSummaryQuery(Guid? BotId, DateOnly From, DateOnly To)
    : IRequest<FeedbackSummaryDto>, IAnalyticsPeriod
{
    public const int MaxBadAnswers = 50;
}

public sealed record FeedbackSummaryDto(int Positive, int Negative, IReadOnlyList<RatedAnswerDto> BadAnswers);

public sealed record RatedAnswerDto(Guid ConversationId, Guid BotId, string Question, string Answer, DateTimeOffset AnsweredAt);

internal sealed class GetFeedbackSummaryQueryValidator : AbstractValidator<GetFeedbackSummaryQuery>
{
    public GetFeedbackSummaryQueryValidator() => this.AddPeriodRules();
}

internal sealed class GetFeedbackSummaryQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetFeedbackSummaryQuery, FeedbackSummaryDto>
{
    public async Task<FeedbackSummaryDto> Handle(GetFeedbackSummaryQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();
        var answers = db.Messages(request).Where(m => m.Role == MessageRole.Assistant);

        var positive = await answers.CountAsync(m => m.Feedback == MessageFeedback.Positive, cancellationToken);
        var negative = await answers.CountAsync(m => m.Feedback == MessageFeedback.Negative, cancellationToken);

        // An answer belongs to the user's last question before it in the same chat.
        var bad = await answers
            .Where(m => m.Feedback == MessageFeedback.Negative)
            .OrderByDescending(m => m.CreatedAt)
            .Take(GetFeedbackSummaryQuery.MaxBadAnswers)
            .Select(a => new RatedAnswerDto(
                a.ConversationId,
                db.Conversations.First(c => c.Id == a.ConversationId).BotId,
                db.Messages
                    .Where(q => q.ConversationId == a.ConversationId && q.Role == MessageRole.User && q.CreatedAt <= a.CreatedAt)
                    .OrderByDescending(q => q.CreatedAt)
                    .Select(q => q.Content)
                    .First(),
                a.Content,
                a.CreatedAt))
            .ToListAsync(cancellationToken);

        return new FeedbackSummaryDto(positive, negative, bad);
    }
}
