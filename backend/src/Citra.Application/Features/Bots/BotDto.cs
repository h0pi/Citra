using Citra.Domain.Bots;

namespace Citra.Application.Features.Bots;

/// <summary>A bot with all its settings, as the bot settings page needs it.</summary>
public sealed record BotDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? SystemPrompt,
    string WelcomeMessage,
    string? Model,
    double Temperature,
    bool IsPublic,
    string WidgetColor,
    IReadOnlyList<string> AllowedDomains,
    IReadOnlyList<string> StarterQuestions,
    int? DailyMessageLimit,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static BotDto From(Bot bot) => new(
        bot.Id,
        bot.Name,
        bot.Slug,
        bot.Description,
        bot.SystemPrompt,
        bot.WelcomeMessage,
        bot.Model,
        bot.Temperature,
        bot.IsPublic,
        bot.WidgetColor,
        bot.AllowedDomains,
        bot.StarterQuestions,
        bot.DailyMessageLimit,
        bot.CreatedAt,
        bot.UpdatedAt);
}
