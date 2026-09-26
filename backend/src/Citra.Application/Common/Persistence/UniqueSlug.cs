using Citra.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Common.Persistence;

/// <summary>Picks a slug that is not taken yet: "acme" if free, otherwise "acme-2", "acme-3", ...</summary>
public static class UniqueSlug
{
    /// <param name="existingSlugs">
    /// Slugs of the rows the new one must not clash with (e.g. <c>db.Bots.Select(b => b.Slug)</c>,
    /// which the tenant filter already limits to the current tenant).
    /// </param>
    public static async Task<string> FindAsync(
        IQueryable<string> existingSlugs,
        string baseSlug,
        CancellationToken cancellationToken)
    {
        var taken = await existingSlugs
            .Where(s => s == baseSlug || s.StartsWith(baseSlug + "-"))
            .ToListAsync(cancellationToken);

        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        var suffix = 2;
        while (taken.Contains($"{baseSlug}-{suffix}"))
        {
            suffix++;
        }

        var candidate = $"{baseSlug}-{suffix}";
        return candidate.Length <= Slug.MaxLength ? candidate : $"{baseSlug[..(Slug.MaxLength - 8)]}-{suffix}";
    }
}
