using Citra.Api.Chat;
using Citra.Application.Features.Chat.SendMessage;
using Citra.Application.Features.Public;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Citra.Api.Controllers;

/// <summary>
/// For the website widget: no login, callable from any website (CORS); each bot then allows only its own
/// websites (403 for others). Only public bots are found; any other bot answers 404. Each IP address may send a
/// limited number of requests a minute (429).
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableCors(CorsPolicies.PublicWidget)]
[EnableRateLimiting(RateLimits.PublicWidget)]
[Route("api/public")]
[Produces("application/json")]
public sealed class PublicController(ISender sender, ILogger<PublicController> logger) : ControllerBase
{
    /// <summary>A public bot's name, welcome message, color and starter questions.</summary>
    [HttpGet("bots/{tenantSlug}/{botSlug}")]
    [ProducesResponseType<PublicBotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PublicBotDto> GetBot(string tenantSlug, string botSlug, CancellationToken cancellationToken) =>
        sender.Send(new GetPublicBotQuery(tenantSlug, botSlug, Origin), cancellationToken);

    /// <summary>The website the request comes from (the browser's Origin header), if any.</summary>
    private string? Origin => Request.Headers.Origin is { Count: > 0 } origin ? origin.ToString() : null;

    /// <summary>
    /// A website visitor asks a public bot a question and gets the answer as server-sent events, the same as the
    /// admin chat (see <see cref="ChatEvents"/>). The visitor id is a random id the widget keeps in the browser;
    /// only that id can continue its conversation.
    /// </summary>
    [HttpPost("bots/{tenantSlug}/{botSlug}/chat")]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<IResult> Chat(
        string tenantSlug, string botSlug, PublicChatRequest request, CancellationToken cancellationToken) =>
        ChatEvents.StreamAsync(
            sender.CreateStream(
                new SendPublicMessageCommand(
                    tenantSlug, botSlug, request.VisitorId ?? string.Empty, request.ConversationId,
                    request.Question ?? string.Empty, Origin),
                cancellationToken),
            logger,
            cancellationToken);
}

/// <summary>Body of POST /api/public/bots/{tenantSlug}/{botSlug}/chat. Without a conversation id a new one starts.</summary>
public sealed record PublicChatRequest(string? VisitorId, Guid? ConversationId, string? Question);
