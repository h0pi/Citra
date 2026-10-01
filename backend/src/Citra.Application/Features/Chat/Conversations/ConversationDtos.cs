using Citra.Application.Common.Persistence;
using Citra.Domain.Conversations;

namespace Citra.Application.Features.Chat.Conversations;

/// <summary>One row of the chat list.</summary>
/// <param name="UpdatedAt">Last activity (the list is sorted by it, newest first).</param>
public sealed record ConversationSummaryDto(
    Guid Id, Guid BotId, string Title, int MessageCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>A conversation with all its messages, oldest first.</summary>
public sealed record ConversationDto(
    Guid Id,
    Guid BotId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ConversationMessageDto> Messages);

/// <summary>A question or an answer. Answers also tell whether the documents had the answer, its rating and its sources.</summary>
public sealed record ConversationMessageDto(
    Guid Id,
    MessageRole Role,
    string Content,
    DateTimeOffset CreatedAt,
    bool WasAnswered,
    MessageFeedback Feedback,
    IReadOnlyList<CitationDto> Citations);

/// <summary>A source an answer cites as [Number]; the chunk and document links are null once they were deleted.</summary>
public sealed record CitationDto(
    int Number, Guid? ChunkId, Guid? DocumentId, string DocumentName, int? PageNumber, string Snippet, double Score);

internal static class OwnConversations
{
    /// <summary>
    /// The current user's own conversations in the admin app (the tenant filter already limits them to the
    /// user's company). Somebody else's conversation is treated as not existing.
    /// </summary>
    public static IQueryable<Conversation> Own(this IApplicationDbContext db, Guid userId) =>
        db.Conversations.Where(c => c.UserId == userId && c.Channel == ConversationChannel.Admin);
}
