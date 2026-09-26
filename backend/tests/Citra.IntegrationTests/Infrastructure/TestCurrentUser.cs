using Citra.Application.Common.Security;
using Citra.Domain.Users;

namespace Citra.IntegrationTests.Infrastructure;

/// <summary>A current user that tests set directly (no token needed).</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public Guid? TenantId { get; set; }

    public Guid? UserId { get; set; }

    public UserRole? Role { get; set; } = UserRole.Admin;

    public bool IsAuthenticated => TenantId is not null;
}
