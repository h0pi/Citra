using Citra.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Citra.Infrastructure.Identity;

/// <summary>
/// A login account (ASP.NET Identity handles email, password hashing, lockout).
/// Every user belongs to exactly one tenant and has a role within it.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;

    public Guid TenantId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }
}
