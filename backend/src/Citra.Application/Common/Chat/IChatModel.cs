namespace Citra.Application.Common.Chat;

/// <summary>
/// A language model that writes answers (Claude, or the free stand-in used until the Anthropic account has
/// credits). The answer arrives in pieces as it is written, so the chat can show it word by word.
/// </summary>
public interface IChatModel
{
    /// <summary>The model that answers when a request names none, e.g. "claude-sonnet-5-5".</summary>
    string DefaultModel { get; }

    /// <summary>
    /// Streams the answer: <see cref="ChatTextDelta"/> pieces in order, then exactly one <see cref="ChatCompleted"/>
    /// with the tokens used. Stops when <paramref name="cancellationToken"/> is cancelled (e.g. the user left).
    /// </summary>
    /// <exception cref="ChatModelException">The model could not be reached or refused the request.</exception>
    IAsyncEnumerable<ChatEvent> StreamAsync(ChatRequest request, CancellationToken cancellationToken);
}

/// <summary>What to send: the rules (system prompt) and the conversation, oldest message first, ending with the user.</summary>
public sealed record ChatRequest(string SystemPrompt, IReadOnlyList<ChatMessage> Messages)
{
    /// <summary>The model to use; null = <see cref="IChatModel.DefaultModel"/>.</summary>
    public string? Model { get; init; }

    /// <summary>Longest answer allowed, in tokens; null = the configured maximum.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Refuses requests no model could answer: no rules, no question last, empty messages, bad settings.</summary>
    public void EnsureValid()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SystemPrompt, nameof(SystemPrompt));
        ArgumentNullException.ThrowIfNull(Messages, nameof(Messages));
        if (Messages.Count == 0 || Messages[^1].Role != ChatRole.User)
        {
            throw new ArgumentException("The conversation must end with the user's message.", nameof(Messages));
        }

        if (Messages.Any(m => string.IsNullOrWhiteSpace(m.Content)))
        {
            throw new ArgumentException("Messages must not be empty.", nameof(Messages));
        }

        if (MaxOutputTokens is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOutputTokens), "Must be at least 1.");
        }
    }
}

public sealed record ChatMessage(ChatRole Role, string Content);

public enum ChatRole
{
    User,
    Assistant,
}

/// <summary>One step of a streamed answer.</summary>
public abstract record ChatEvent;

/// <summary>The next piece of the answer's text.</summary>
public sealed record ChatTextDelta(string Text) : ChatEvent;

/// <summary>The answer is complete.</summary>
/// <param name="StopReason">Why it ended, e.g. "end_turn" (finished) or "max_tokens" (cut off at the limit).</param>
public sealed record ChatCompleted(ChatUsage Usage, string? StopReason) : ChatEvent;

/// <summary>Which model answered and the tokens it billed (question and context in, answer out).</summary>
public sealed record ChatUsage(string Model, int InputTokens, int OutputTokens);

/// <summary>
/// The model failed: not reachable, overloaded or rate-limited after retries, refused the key or the request.
/// The message is for logs; users are told the AI service is not available.
/// </summary>
public sealed class ChatModelException(string message, Exception? innerException = null)
    : Exception(message, innerException);
