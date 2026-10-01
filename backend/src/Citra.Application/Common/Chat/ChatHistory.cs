using Citra.Application.Common.Chunking;

namespace Citra.Application.Common.Chat;

/// <summary>
/// Chooses which earlier messages go with a new question. Recent messages matter most, so the newest are kept and
/// the oldest dropped, until the message limit or the token budget is reached. The kept part always starts with a
/// question (a model expects the user to speak first) and is never cut inside a message.
/// </summary>
public static class ChatHistory
{
    /// <param name="history">Earlier messages, oldest first, without the new question.</param>
    public static IReadOnlyList<ChatMessage> Trim(
        IReadOnlyList<ChatMessage> history, int maxMessages, int maxTokens, ITokenCounter tokens)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMessages);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTokens);

        var start = history.Count;
        var used = 0;
        while (start > 0 && history.Count - start < maxMessages)
        {
            var size = tokens.Count(history[start - 1].Content);
            if (used + size > maxTokens)
            {
                break;
            }

            used += size;
            start--;
        }

        // An answer without its question would confuse the model: start at a question.
        while (start < history.Count && history[start].Role != ChatRole.User)
        {
            start++;
        }

        return [.. history.Skip(start)];
    }
}
