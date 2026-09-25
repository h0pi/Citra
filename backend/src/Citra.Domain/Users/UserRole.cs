namespace Citra.Domain.Users;

/// <summary>
/// What a user may do inside their tenant. The user account itself is an ASP.NET Identity user
/// (Infrastructure/Identity/ApplicationUser), linked to a tenant by TenantId.
/// </summary>
public enum UserRole
{
    /// <summary>Created the tenant; full access including account settings.</summary>
    Owner = 0,

    /// <summary>Manages bots, documents and settings.</summary>
    Admin = 1,

    /// <summary>Can chat and view, cannot change or delete.</summary>
    Viewer = 2,

    /// <summary>Public demo login: read-only, like Viewer, and reset nightly.</summary>
    Demo = 3,
}
