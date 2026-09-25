namespace Citra.Domain.Tenants;

public enum TenantPlan
{
    Free = 0,
    Pro = 1,

    /// <summary>The public read-only demo workspace.</summary>
    Demo = 2,
}
