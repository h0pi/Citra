namespace Citra.Application.Common.Persistence;

/// <summary>Names of the automatic query filters, for the rare code that must bypass one explicitly.</summary>
public static class QueryFilters
{
    /// <summary>
    /// Limits every query on tenant-owned data (ITenantOwned) to the current user's tenant.
    /// Fails closed: with no logged-in user, queries return nothing. Code that legitimately works across
    /// tenants (e.g. the background worker) must opt out explicitly with
    /// <c>.IgnoreQueryFilters([QueryFilters.Tenant])</c> — visible and easy to review.
    /// </summary>
    public const string Tenant = "Tenant";
}
