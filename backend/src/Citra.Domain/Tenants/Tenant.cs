using Citra.Domain.Common;

namespace Citra.Domain.Tenants;

/// <summary>A customer account (company/workspace). All bots, documents and chats belong to one tenant.</summary>
public sealed class Tenant : Entity
{
    public const int NameMaxLength = 100;

    public string Name { get; private set; } = string.Empty;

    /// <summary>URL-friendly unique identifier, e.g. "acme-logistics".</summary>
    public string Slug { get; private set; } = string.Empty;

    public TenantPlan Plan { get; private set; }

    private Tenant()
    {
        // For EF Core.
    }

    public static Tenant Create(string name, TenantPlan plan = TenantPlan.Free)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Tenant
        {
            Name = name.Trim(),
            Slug = Common.Slug.From(name),
            Plan = plan,
        };
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void ChangePlan(TenantPlan plan) => Plan = plan;
}
