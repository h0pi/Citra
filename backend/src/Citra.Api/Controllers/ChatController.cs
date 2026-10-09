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
    public Task<IResult> Send(Guid botId, SendMessageRequest request, CancellationToken cancellationToken) =>
        ChatEvents.StreamAsync(
            sender.CreateStream(
                new SendMessageCommand(botId, request.ConversationId, request.Question ?? string.Empty), cancellationToken),
            logger,
            cancellationToken);
}

/// <summary>Body of POST /api/bots/{botId}/chat. Without a conversation id a new conversation starts.</summary>
public sealed record SendMessageRequest(Guid? ConversationId, string? Question);
