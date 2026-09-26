using Citra.Application.Common.Security;
using Citra.Domain.Users;

namespace Citra.Infrastructure.Identity;

/// <summary>
/// Default <see cref="ICurrentUser"/> for code that runs without a web request (the Worker, startup tasks):
/// nobody is logged in. The API replaces it with one that reads the request's access token.
/// </summary>
internal sealed class NoCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public Guid? TenantId => null;

    public UserRole? Role => null;
}
