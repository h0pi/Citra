using Citra.Domain.Users;

namespace Citra.Application.Features.Auth;

/// <summary>The logged-in user, as the frontend needs it.</summary>
public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    UserRole Role,
    Guid TenantId,
    string TenantName);

/// <summary>Returned by register, login and refresh: both tokens plus who is logged in.</summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    CurrentUserDto User);
