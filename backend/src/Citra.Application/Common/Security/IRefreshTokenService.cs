namespace Citra.Application.Common.Security;

/// <summary>A refresh token as handed to the client (the raw value exists only here, never in the database).</summary>
public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Result of a successful refresh: whose token it was, and the replacement token.</summary>
public sealed record RefreshTokenRotation(Guid UserId, IssuedRefreshToken NewToken);

/// <summary>
/// Long-lived refresh tokens that get a new access token without the password.
/// Each token works once (rotation); reusing an old one revokes the whole login session (theft detection).
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Starts a new login session (a new token "family") for the user.</summary>
    Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a valid token for a new one. Returns null if the token is unknown, expired or revoked —
    /// and if it was already used, the whole session is revoked because the token was probably stolen.
    /// </summary>
    Task<RefreshTokenRotation?> RotateAsync(string token, CancellationToken cancellationToken);

    /// <summary>Logout: revokes the token's whole session. Unknown tokens are ignored.</summary>
    Task RevokeAsync(string token, CancellationToken cancellationToken);
}
