using Citra.Application.Common.Persistence;
using Citra.Domain.Bots;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Bots;

/// <summary>
/// A bot with all its settings, as the bot settings page needs it. <see cref="TenantSlug"/> and
/// <see cref="Slug"/> together are the bot's widget address ("acme/support").
/// </summary>
public sealed record BotDto(
    Guid Id,
    string Name,
    string Slug,
    string TenantSlug,
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
    public static async Task<BotDto> FromAsync(Bot bot, IApplicationDbContext db, CancellationToken cancellationToken) => new(
        bot.Id,
        bot.Name,
        bot.Slug,
        await db.Tenants.Where(t => t.Id == bot.TenantId).Select(t => t.Slug).SingleAsync(cancellationToken),
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
