using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Conversations;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Analytics;

/// <summary>
/// "Knowledge gaps": questions of the period the documents could not answer, newest first, each with its chat so it
/// can be opened. They show which documents are missing.
/// </summary>
public sealed record GetUnansweredQuestionsQuery(
    Guid? BotId, DateOnly From, DateOnly To, int Limit = GetUnansweredQuestionsQuery.DefaultLimit)
    : IRequest<IReadOnlyList<UnansweredQuestionDto>>, IAnalyticsPeriod
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
}

public sealed record UnansweredQuestionDto(
    Guid ConversationId, Guid BotId, string Question, ConversationChannel Channel, DateTimeOffset AskedAt);

internal sealed class GetUnansweredQuestionsQueryValidator : AbstractValidator<GetUnansweredQuestionsQuery>
{
    public GetUnansweredQuestionsQueryValidator()
    {
        this.AddPeriodRules();
        RuleFor(x => x.Limit).InclusiveBetween(1, GetUnansweredQuestionsQuery.MaxLimit);
    }
}

internal sealed class GetUnansweredQuestionsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetUnansweredQuestionsQuery, IReadOnlyList<UnansweredQuestionDto>>
{
    public async Task<IReadOnlyList<UnansweredQuestionDto>> Handle(
        GetUnansweredQuestionsQuery request, CancellationToken cancellationToken)
    {
        currentUser.RequireTenantId();

        // An answer belongs to the user's last question before it in the same chat.
        return await db.Messages(request)
            .Where(a => a.Role == MessageRole.Assistant && !a.WasAnswered)
            .OrderByDescending(a => a.CreatedAt)
            .Take(request.Limit)
            .Select(a => new
            {
                Conversation = db.Conversations.First(c => c.Id == a.ConversationId),
                Question = db.Messages
                    .Where(q => q.ConversationId == a.ConversationId && q.Role == MessageRole.User && q.CreatedAt <= a.CreatedAt)
                    .OrderByDescending(q => q.CreatedAt)
                    .First(),
            })
            .Select(g => new UnansweredQuestionDto(
                g.Conversation.Id, g.Conversation.BotId, g.Question.Content, g.Conversation.Channel, g.Question.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
