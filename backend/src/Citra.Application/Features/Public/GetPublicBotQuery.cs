using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Domain.Bots;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Public;

/// <summary>
/// What the website widget needs to show a bot, without a login. Bot slugs are unique within a company,
/// so the company's slug comes first (e.g. "acme/support"). Only public bots are found, and only from the websites
/// the bot allows (<paramref name="Origin"/> is the browser's Origin header).
/// </summary>
public sealed record GetPublicBotQuery(string TenantSlug, string BotSlug, string? Origin = null) : IRequest<PublicBotDto>;

public sealed record PublicBotDto(
    string Name,
    string WelcomeMessage,
    string WidgetColor,
    IReadOnlyList<string> StarterQuestions);

internal sealed class GetPublicBotQueryHandler(IApplicationDbContext db) : IRequestHandler<GetPublicBotQuery, PublicBotDto>
{
    public async Task<PublicBotDto> Handle(GetPublicBotQuery request, CancellationToken cancellationToken)
    {
        var bot = await db.PublicBot(request.TenantSlug, request.BotSlug).AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Bot), $"{request.TenantSlug}/{request.BotSlug}");
        PublicBots.EnsureOriginAllowed(bot, request.Origin);

        return new PublicBotDto(bot.Name, bot.WelcomeMessage, bot.WidgetColor, bot.StarterQuestions);
    }
}

public static class PublicBots
{
    /// <summary>
    /// A public bot, found by its company's slug and its own (bot slugs are unique only within a company).
    /// There is no user on public requests, so the tenant filter is lifted: the slugs name the company.
    /// </summary>
    internal static IQueryable<Bot> PublicBot(this IApplicationDbContext db, string tenantSlug, string botSlug) =>
        db.Bots
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(b => b.IsPublic
                && b.Slug == botSlug
                && db.Tenants.Any(t => t.Id == b.TenantId && t.Slug == tenantSlug));

    /// <summary>
    /// The widget works only on the websites a bot allows: the listed domains and their subdomains ("acme.com"
    /// also allows "shop.acme.com"). A bot without a list allows every website. Browsers always send the Origin
    /// header; a request without one is refused when there is a list.
    /// </summary>
    internal static void EnsureOriginAllowed(Bot bot, string? origin)
    {
        if (bot.AllowedDomains.Count == 0)
        {
            return;
        }

        var host = Uri.TryCreate(origin, UriKind.Absolute, out var uri) ? uri.Host : null; // Uri gives the host in lower case
        if (host is null || !bot.AllowedDomains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal)))
        {
            throw new ForbiddenException("This website is not allowed to use this bot.");
        }
    }
}
