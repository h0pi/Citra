using MediatR;

namespace Citra.Application.Features.Bots.Update;

/// <summary>
/// Saves the bot settings form. All fields are sent every time (the form always has them all).
/// The slug is not changed on rename: it is part of widget URLs already embedded on customers' websites.
/// <paramref name="IsPublic"/>, <paramref name="AllowedDomains"/> (empty = every website) and
/// <paramref name="DailyMessageLimit"/> (null = unlimited) control the website widget.
/// </summary>
public sealed record UpdateBotCommand(
    Guid Id,
    string Name,
    string? Description,
    string? SystemPrompt,
    string WelcomeMessage,
    string? Model,
    double Temperature,
    string WidgetColor,
    IReadOnlyList<string> StarterQuestions,
    bool IsPublic,
    IReadOnlyList<string> AllowedDomains,
    int? DailyMessageLimit) : IRequest<BotDto>;
