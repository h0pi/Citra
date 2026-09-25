namespace Citra.Domain.Common;

/// <summary>
/// Marks data that belongs to one tenant (customer account). The persistence layer
/// uses this to automatically hide other tenants' rows from every query.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
