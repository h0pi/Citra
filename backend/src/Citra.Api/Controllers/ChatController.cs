using Citra.Api.Chat;
using Citra.Application.Features.Chat.SendMessage;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citra.Api.Controllers;

/// <summary>Chatting with a bot in the admin app. Every role may chat; conversations are private to their user.</summary>
[ApiController]
[Authorize]
[Route("api/bots/{botId:guid}/chat")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class ChatController(ISender sender, ILogger<ChatController> logger) : ControllerBase
{
    /// <summary>
    /// Asks the bot a question (a new conversation, or continuing one) and streams the answer as server-sent events
    /// (see <see cref="ChatEvents"/>). Checks, saving the question and the search happen before streaming starts,
    /// so their failures are ordinary HTTP errors (400, 404, 503); later failures arrive as an <c>error</c> event.
    /// Closing the connection stops the answer (and the model) at once.
    /// </summary>
    [HttpPost]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IResult> Send(Guid botId, SendMessageRequest request, CancellationToken cancellationToken)
    {
        // cancellationToken is the request's: it fires when the browser disconnects, which cancels the model call.
        var answer = sender
            .CreateStream(new SendMessageCommand(botId, request.ConversationId, request.Question ?? string.Empty), cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        bool started;
        try
        {
            started = await answer.MoveNextAsync();
        }
        catch
        {
            await answer.DisposeAsync();
            throw; // still before the response started: the exception handler turns it into 400/404/503
        }

        if (!started)
        {
            await answer.DisposeAsync();
            throw new InvalidOperationException("The chat ended before it started.");
        }

        return TypedResults.ServerSentEvents(ChatEvents.ToSse(answer.Current, answer, logger, cancellationToken));
    }
}

/// <summary>Body of POST /api/bots/{botId}/chat. Without a conversation id a new conversation starts.</summary>
public sealed record SendMessageRequest(Guid? ConversationId, string? Question);
