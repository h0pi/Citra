using MediatR;

namespace Citra.Application.Features.Bots.Create;

/// <summary>
/// Creates a bot in the current user's company. Only the basics are asked for up front;
/// prompt, model and widget settings start with sensible defaults and are changed on the settings page.
/// </summary>
public sealed record CreateBotCommand(string Name, string? Description) : IRequest<BotDto>;
