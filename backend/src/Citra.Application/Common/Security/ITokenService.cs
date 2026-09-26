using Citra.Domain.Users;

namespace Citra.Application.Common.Security;

/// <summary>Who the access token is for.</summary>
public sealed record TokenUser(Guid UserId, Guid TenantId, string Email, string DisplayName, UserRole Role);

/// <summary>A signed access token and when it stops being valid.</summary>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Creates the short-lived access token a user gets after logging in.</summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(TokenUser user);
}
