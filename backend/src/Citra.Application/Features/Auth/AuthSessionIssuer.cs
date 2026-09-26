using Citra.Application.Common.Security;

namespace Citra.Application.Features.Auth;

/// <summary>
/// The one place that turns a verified user into a logged-in session (access + refresh token + user info).
/// Shared by register, login and refresh so their responses can never drift apart.
/// </summary>
internal sealed class AuthSessionIssuer(ITokenService tokens, IRefreshTokenService refreshTokens)
{
    /// <summary>New login: starts a new refresh token family.</summary>
    public async Task<AuthResponse> StartSessionAsync(
        UserAccount account,
        string tenantName,
        CancellationToken cancellationToken)
    {
        var refreshToken = await refreshTokens.IssueAsync(account.UserId, cancellationToken);
        return Build(account, tenantName, refreshToken);
    }

    /// <summary>Refresh: the rotated refresh token continues the existing family.</summary>
    public AuthResponse ContinueSession(UserAccount account, string tenantName, IssuedRefreshToken refreshToken) =>
        Build(account, tenantName, refreshToken);

    private AuthResponse Build(UserAccount account, string tenantName, IssuedRefreshToken refreshToken)
    {
        var accessToken = tokens.CreateAccessToken(
            new TokenUser(account.UserId, account.TenantId, account.Email, account.DisplayName, account.Role));

        return new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAt,
            refreshToken.Token,
            refreshToken.ExpiresAt,
            new CurrentUserDto(
                account.UserId,
                account.Email,
                account.DisplayName,
                account.Role,
                account.TenantId,
                tenantName));
    }
}
