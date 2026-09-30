using Citra.Application.Common.Exceptions;
using Citra.Domain.Users;

namespace Citra.Application.Common.Security;

/// <summary>
/// Who is making the current request, taken from the verified access token.
/// In the API this reads the request's claims; in background work (Worker) there is no user.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    Guid? TenantId { get; }

    UserRole? Role { get; }
}

public static class CurrentUserExtensions
{
    public static Guid RequireUserId(this ICurrentUser user) =>
        user.UserId ?? throw new UnauthorizedException("You need to be logged in.");

    public static Guid RequireTenantId(this ICurrentUser user) =>
        user.TenantId ?? throw new UnauthorizedException("You need to be logged in.");

    /// <summary>
    /// For actions that change the tenant's bots, documents or settings: only Owner and Admin may.
    /// Viewer and the read-only Demo account get 403. Returns the tenant the change belongs to.
    /// </summary>
    public static Guid RequireEditorTenantId(this ICurrentUser user)
    {
        var tenantId = user.RequireTenantId();
        if (user.Role is not (UserRole.Owner or UserRole.Admin))
        {
            throw new ForbiddenException("Your role can view but not change this.");
        }

        return tenantId;
    }

    /// <summary>
    /// For admin tools (e.g. the search playground, which shows how retrieval scores): only Owner and Admin.
    /// Returns the user's tenant.
    /// </summary>
    public static Guid RequireAdminTenantId(this ICurrentUser user)
    {
        var tenantId = user.RequireTenantId();
        if (user.Role is not (UserRole.Owner or UserRole.Admin))
        {
            throw new ForbiddenException("Only owners and admins can use this.");
        }

        return tenantId;
    }
}
