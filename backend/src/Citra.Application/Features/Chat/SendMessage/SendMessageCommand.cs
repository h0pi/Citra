using Citra.Domain.Conversations;
using FluentValidation;
using MediatR;

namespace Citra.Application.Features.Chat.SendMessage;

/// <summary>
/// Asks a bot a question in the admin app's chat and streams the answer. Without a conversation id a new
/// conversation starts; with one, the question continues it (and the earlier messages give it context).
/// </summary>
public sealed record SendMessageCommand(Guid BotId, Guid? ConversationId, string Question)
    : IStreamRequest<SendMessageEvent>
{
    public const int QuestionMaxLength = 4000;
}

/// <summary>
/// Asks a public bot a question from the website widget, without a login. A visitor is known only by the random
/// id the widget keeps in the browser; a conversation can only be continued with the same visitor id. Only
/// websites the bot allows may ask (<paramref name="Origin"/> is the browser's Origin header).
/// </summary>
public sealed record SendPublicMessageCommand(
    string TenantSlug, string BotSlug, string VisitorId, Guid? ConversationId, string Question, string? Origin = null)
    : IStreamRequest<SendMessageEvent>
{
    /// <summary>A visitor may ask this many questions a minute (the API also limits each IP address, more loosely).</summary>
    public const int QuestionsPerVisitorPerMinute = 10;
}

/// <summary>What happens while a question is answered, in this order: started, sources, tokens…, completed.</summary>
public abstract record SendMessageEvent;

/// <summary>The question is saved; the answer is being prepared.</summary>
/// <param name="SearchQuery">What was searched: the question, or its standalone rewrite for a follow-up.</param>
public sealed record MessageStarted(Guid ConversationId, Guid QuestionId, string SearchQuery) : SendMessageEvent;

/// <summary>
/// The passages the model answers from, numbered as the answer cites them ([1], [2], …), so the page can link the
/// markers while the answer is still being written.
/// </summary>
public sealed record SourcesFound(IReadOnlyList<AnswerSource> Sources) : SendMessageEvent;

/// <summary>One numbered passage: where it is (document, page, exact offsets) and what it says.</summary>
public sealed record AnswerSource(
    int Number,
    Guid ChunkId,
    Guid DocumentId,
    string DocumentName,
    int? PageNumber,
    string? Heading,
    string Content,
    int CharStart,
    int CharEnd,
    double Score);

/// <summary>The next piece of the answer.</summary>
public sealed record AnswerToken(string Text) : SendMessageEvent;

/// <summary>The whole answer, as saved (it replaces the streamed pieces).</summary>
/// <param name="WasAnswered">False when the documents did not contain the answer.</param>
/// <param name="CitedSources">Numbers of the sources the answer cites (the others were only offered).</param>
public sealed record AnswerCompleted(Guid MessageId, string Content, bool WasAnswered, IReadOnlyList<int> CitedSources)
    : SendMessageEvent;

internal sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(x => x.BotId).NotEmpty();
        RuleFor(x => x.ConversationId).NotEqual(Guid.Empty);
        RuleFor(x => x.Question).NotEmpty().MaximumLength(SendMessageCommand.QuestionMaxLength);
    }
}

internal sealed class SendPublicMessageCommandValidator : AbstractValidator<SendPublicMessageCommand>
{
    public SendPublicMessageCommandValidator()
    {
        RuleFor(x => x.VisitorId).NotEmpty().MaximumLength(Conversation.VisitorIdMaxLength);
        RuleFor(x => x.ConversationId).NotEqual(Guid.Empty);
        RuleFor(x => x.Question).NotEmpty().MaximumLength(SendMessageCommand.QuestionMaxLength);
    }
}
