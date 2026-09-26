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
}
