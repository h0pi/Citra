using System.Text;
using Citra.Application.Common.Chunking;
using Microsoft.Extensions.Logging;

namespace Citra.Application.Common.Chat;

/// <summary>
/// Turns a follow-up question into one that makes sense on its own, so the search finds the right passages:
/// after "How many vacation days do I get?", the question "And for part-time?" is searched as
/// "How many vacation days do part-time employees get?".
/// </summary>
public interface IQueryRewriter
{
    /// <param name="history">Earlier messages of the conversation, oldest first (without <paramref name="question"/>).</param>
    Task<RewrittenQuery> RewriteAsync(
        IReadOnlyList<ChatMessage> history, string question, CancellationToken cancellationToken);
}

/// <summary>The question to search with, and what rewriting it cost (null when no model was asked).</summary>
public sealed record RewrittenQuery(string Query, ChatUsage? Usage);

/// <summary>
/// Asks the cheap rewrite model (<see cref="IChatSettings.RewriteModel"/>). A first question needs no rewriting
/// and costs nothing. Rewriting only improves the search, so when it fails or returns something unusable, the
/// original question is searched instead of failing the chat.
/// </summary>
internal sealed class QueryRewriter(
    IChatModel model,
    IChatSettings settings,
    ITokenCounter tokens,
    ILogger<QueryRewriter> logger) : IQueryRewriter
{
    /// <summary>A rewritten question longer than this is not a question any more (the model answered instead).</summary>
    internal const int MaxLength = 1000;

    internal const int MaxOutputTokens = 200;

    internal const string SystemPrompt = """
        You rewrite a user's latest question so it can be understood without the conversation before it. Replace
        words like "it", "that" or "and for ..." with what they refer to in the conversation. Keep the question's
        language and meaning; do not answer it and do not add anything new. If it is already clear on its own,
        return it unchanged. Output only the question, nothing else.
        """;

    public async Task<RewrittenQuery> RewriteAsync(
        IReadOnlyList<ChatMessage> history, string question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        question = question.Trim();

        var context = ChatHistory.Trim(history, settings.MaxHistoryMessages, settings.MaxHistoryTokens, tokens);
        if (context.Count == 0)
        {
            return new RewrittenQuery(question, null);
        }

        var request = new ChatRequest(SystemPrompt, [new ChatMessage(ChatRole.User, Prompt(context, question))])
        {
            Model = settings.RewriteModel,
            Temperature = 0,
            MaxOutputTokens = MaxOutputTokens,
        };

        var text = new StringBuilder();
        ChatUsage? usage = null;
        try
        {
            await foreach (var chatEvent in model.StreamAsync(request, cancellationToken))
            {
                switch (chatEvent)
                {
                    case ChatTextDelta delta:
                        text.Append(delta.Text);
                        break;
                    case ChatCompleted completed:
                        usage = completed.Usage;
                        break;
                }
            }
        }
        catch (ChatModelException ex)
        {
            logger.LogWarning(ex, "Could not rewrite a follow-up question; searching with the original question");
            return new RewrittenQuery(question, usage);
        }

        var rewritten = Clean(text.ToString());
        return new RewrittenQuery(rewritten.Length is > 0 and <= MaxLength ? rewritten : question, usage);
    }

    /// <summary>The conversation and the question, escaped like the sources so a message cannot fake a tag.</summary>
    internal static string Prompt(IReadOnlyList<ChatMessage> context, string question)
    {
        var prompt = new StringBuilder("<conversation>\n");
        foreach (var message in context)
        {
            prompt.Append(message.Role == ChatRole.User ? "User: " : "Assistant: ")
                .Append(SourceList.Escape(message.Content.Trim()))
                .Append('\n');
        }

        return prompt.Append("</conversation>\n\n<question>\n")
            .Append(SourceList.Escape(question))
            .Append("\n</question>")
            .ToString();
    }

    /// <summary>Models sometimes wrap the answer in quotes or a label; keep just the question.</summary>
    private static string Clean(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("Question:", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned["Question:".Length..].TrimStart();
        }

        if (cleaned.Length >= 2 && cleaned[0] is '"' or '“' && cleaned[^1] is '"' or '”')
        {
            cleaned = cleaned[1..^1].Trim();
        }

        return cleaned;
    }
}
