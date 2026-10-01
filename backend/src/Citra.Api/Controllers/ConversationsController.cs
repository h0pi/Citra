using Citra.Application.Common.Paging;
using Citra.Application.Features.Chat.Conversations;
using Citra.Application.Features.Chat.Feedback;
using Citra.Domain.Conversations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citra.Api.Controllers;

/// <summary>
/// The current user's chats: the list per bot, one chat with its messages, deleting a chat and rating answers.
/// Chats are private: another user's chat answers 404, like one that does not exist.
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class ConversationsController(ISender sender) : ControllerBase
{
    /// <summary>The user's chats with a bot, most recently active first.</summary>
    [HttpGet("bots/{botId:guid}/conversations")]
    [ProducesResponseType<PagedResult<ConversationSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<ConversationSummaryDto>> GetAll(
        Guid botId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetConversationsQuery.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetConversationsQuery(botId, page, pageSize), cancellationToken);

    /// <summary>One chat with all its messages, oldest first, and the sources each answer cites.</summary>
    [HttpGet("conversations/{id:guid}")]
    [ProducesResponseType<ConversationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ConversationDto> GetById(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetConversationQuery(id), cancellationToken);

    /// <summary>Deletes a chat with all its messages.</summary>
    [HttpDelete("conversations/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteConversationCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Thumbs up ("Positive"), down ("Negative") or no rating ("None") for an answer.</summary>
    [HttpPut("messages/{id:guid}/feedback")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rate(Guid id, RateAnswerRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new RateAnswerCommand(id, request.Feedback), cancellationToken);
        return NoContent();
    }
}

/// <summary>Body of PUT /api/messages/{id}/feedback.</summary>
public sealed record RateAnswerRequest(MessageFeedback Feedback);
