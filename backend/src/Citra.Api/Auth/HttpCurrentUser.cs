using System.Security.Claims;
using Citra.Application.Common.Security;
using Citra.Domain.Users;

namespace Citra.Api.Auth;

/// <summary>
/// <see cref="ICurrentUser"/> for API requests: reads the claims of the access token that the
/// authentication middleware already verified (signature, expiry, issuer). Unauthenticated → all null.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => ReadGuid(CitraClaimTypes.UserId);

    public Guid? TenantId => ReadGuid(CitraClaimTypes.TenantId);

    public UserRole? Role =>
        IsAuthenticated && Enum.TryParse<UserRole>(Principal!.FindFirstValue(CitraClaimTypes.Role), out var role)
            ? role
            : null;

    private Guid? ReadGuid(string claimType) =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(claimType), out var value)
            ? value
            : null;
}
