namespace Citra.Application.Common.Security;

/// <summary>
/// Claim names inside Citra's access tokens. Short standard JWT names are used as-is
/// (the API turns off .NET's mapping of them to long XML-style URIs).
/// </summary>
public static class CitraClaimTypes
{
    /// <summary>User id (standard JWT "subject").</summary>
    public const string UserId = "sub";

    public const string Email = "email";

    public const string DisplayName = "name";

    public const string Role = "role";

    /// <summary>The tenant (company) the user belongs to — used to isolate every query.</summary>
    public const string TenantId = "tenant_id";
}
