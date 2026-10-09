using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Citra.Application.Common.Chat;
using Citra.Application.Common.Embeddings;
using Citra.Application.Features.Chat.SendMessage;

namespace Citra.Api.Chat;

/// <summary>
/// Turns a streamed answer into server-sent events for the browser. Each event has a type and JSON data:
/// <list type="bullet">
/// <item><c>start</c> {conversationId, questionId, searchQuery}: the question is saved.</item>
/// <item><c>citations</c> {sources: [{number, documentName, pageNumber, …}]}: the numbered passages it is answered from.</item>
/// <item><c>token</c> {text}: the next piece of the answer.</item>
/// <item><c>done</c> {messageId, content, wasAnswered, citedSources}: the complete answer, saved; it replaces the tokens.</item>
/// <item><c>error</c> {message}: answering failed after streaming had started; nothing follows.</item>
/// </list>
/// </summary>
public static class ChatEvents
{
    public const string Start = "start";
    public const string Citations = "citations";
    public const string Token = "token";
    public const string Done = "done";
    public const string Error = "error";

    public const string UnavailableMessage = "The AI service is not available right now. Please try again in a minute.";
    public const string FailedMessage = "Something went wrong while answering. Please try again.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Streams an answer as server-sent events. The first event is read before the response starts, so checks,
    /// saving the question and the search fail as ordinary HTTP errors (400, 404, 503); later failures arrive as
    /// an <c>error</c> event. <paramref name="cancellationToken"/> is the request's: closing the connection stops
    /// the answer (and the model) at once.
    /// </summary>
    public static async Task<IResult> StreamAsync(
        IAsyncEnumerable<SendMessageEvent> answer, ILogger logger, CancellationToken cancellationToken)
    {
        var events = answer.GetAsyncEnumerator(cancellationToken);

        bool started;
        try
        {
            started = await events.MoveNextAsync();
        }
        catch
        {
            await events.DisposeAsync();
            throw; // still before the response started: the exception handler turns it into 400/404/503
        }

        if (!started)
        {
            await events.DisposeAsync();
            throw new InvalidOperationException("The chat ended before it started.");
        }

        return TypedResults.ServerSentEvents(ToSse(events.Current, events, logger, cancellationToken));
    }

    /// <summary>
    /// The events, starting with <paramref name="first"/> (already read by the caller). A failure after that point
    /// cannot change the HTTP status any more (200 was sent), so it becomes a final <c>error</c> event. A cancelled
    /// request (the user left) simply ends.
    /// </summary>
    public static async IAsyncEnumerable<SseItem<string>> ToSse(
        SendMessageEvent first,
        IAsyncEnumerator<SendMessageEvent> rest,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using (rest)
        {
            yield return ToItem(first);

            while (true)
            {
                SseItem<string>? next = null;
                string? failure = null;
                try
                {
                    if (await rest.MoveNextAsync())
                    {
                        next = ToItem(rest.Current);
                    }
                }
                catch (Exception ex) when (ex is ChatModelException or EmbeddingException)
                {
                    logger.LogWarning(ex, "The AI service failed while answering a chat question");
                    failure = UnavailableMessage;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Answering a chat question failed");
                    failure = FailedMessage;
                }

                if (next is { } item)
                {
                    yield return item;
                }
                else
                {
                    if (failure is not null)
                    {
                        yield return Item(Error, new { message = failure });
                    }

                    yield break;
                }
            }
        }
    }

    public static SseItem<string> ToItem(SendMessageEvent message) => message switch
    {
        MessageStarted started => Item(Start, new { started.ConversationId, started.QuestionId, started.SearchQuery }),
        SourcesFound found => Item(Citations, new { found.Sources }),
        AnswerToken token => Item(Token, new { token.Text }),
        AnswerCompleted completed => Item(
            Done, new { completed.MessageId, completed.Content, completed.WasAnswered, completed.CitedSources }),
        _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, "Unknown chat event."),
    };

    private static SseItem<string> Item(string type, object data) => new(JsonSerializer.Serialize(data, Json), type);
}
