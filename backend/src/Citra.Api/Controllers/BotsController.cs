using Citra.Application.Features.Bots;
using Citra.Application.Features.Bots.Create;
using Citra.Application.Features.Bots.Delete;
using Citra.Application.Features.Bots.GetById;
using Citra.Application.Features.Bots.List;
using Citra.Application.Features.Bots.Search;
using Citra.Application.Features.Bots.Update;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citra.Api.Controllers;

/// <summary>
/// The company's bots. Every logged-in user may view them; only Owner and Admin may change them
/// (enforced in the handlers, which return 403 for Viewer and Demo).
/// </summary>
[ApiController]
[Authorize]
[Route("api/bots")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class BotsController(ISender sender) : ControllerBase
{
    /// <summary>All bots of the company, sorted by name, with document and message counts.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BotSummaryDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<BotSummaryDto>> GetAll(CancellationToken cancellationToken) =>
        sender.Send(new GetBotsQuery(), cancellationToken);

    /// <summary>One bot with all its settings.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<BotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<BotDto> GetById(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetBotByIdQuery(id), cancellationToken);

    /// <summary>Claude models a bot can be switched to (for the settings dropdown). No model = app default.</summary>
    [HttpGet("models")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public IReadOnlyList<string> GetModels() => BotModels.Supported;

    /// <summary>Creates a bot with default settings.</summary>
    [HttpPost]
    [ProducesResponseType<BotDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BotDto>> Create(CreateBotCommand command, CancellationToken cancellationToken)
    {
        var bot = await sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = bot.Id }, bot);
    }

    /// <summary>Saves the settings form. The slug stays the same on rename (widget URLs keep working).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<BotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<BotDto> Update(Guid id, UpdateBotRequest request, CancellationToken cancellationToken) =>
        sender.Send(
            new UpdateBotCommand(
                id,
                request.Name,
                request.Description,
                request.SystemPrompt,
                request.WelcomeMessage,
                request.Model,
                request.Temperature,
                request.WidgetColor,
                request.StarterQuestions),
            cancellationToken);

    /// <summary>
    /// Search playground (Owner and Admin): the passages the bot would use to answer <paramref name="q"/>, best
    /// first, with every score. Costs the question's embedding tokens.
    /// </summary>
    [HttpGet("{id:guid}/search")]
    [ProducesResponseType<BotSearchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<BotSearchResult> Search(
        Guid id, [FromQuery] string? q, [FromQuery] int topK = SearchBotQuery.DefaultTopK, CancellationToken cancellationToken = default) =>
        sender.Send(new SearchBotQuery(id, q ?? string.Empty, topK), cancellationToken);

    /// <summary>Permanently deletes the bot with its documents, chats, evals and files.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteBotCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>Body of PUT /api/bots/{id}; the id comes from the URL, so it cannot disagree with the body.</summary>
public sealed record UpdateBotRequest(
    string Name,
    string? Description,
    string? SystemPrompt,
    string WelcomeMessage,
    string? Model,
    double Temperature,
    string WidgetColor,
    IReadOnlyList<string> StarterQuestions);
