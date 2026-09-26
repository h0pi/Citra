using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Auth.Refresh;

internal sealed class RefreshTokenCommandHandler(
    IApplicationDbContext db,
    IIdentityService identity,
    IRefreshTokenService refreshTokens,
    AuthSessionIssuer sessions) : IRequestHandler<RefreshTokenCommand, AuthResponse>
{
    public const string SessionExpiredMessage = "Your session has expired. Please log in again.";

    public async Task<AuthResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        // Unknown, expired, revoked or reused (→ whole session revoked) all end the same way for the client.
        var rotation = await refreshTokens.RotateAsync(request.RefreshToken, cancellationToken)
            ?? throw new UnauthorizedException(SessionExpiredMessage);

        // Load the user fresh, so the new access token carries current data (e.g. a changed role),
        // and a deleted account can't keep refreshing.
        var account = await identity.FindByIdAsync(rotation.UserId, cancellationToken);
        if (account is null)
        {
            await refreshTokens.RevokeAsync(rotation.NewToken.Token, cancellationToken);
            throw new UnauthorizedException(SessionExpiredMessage);
        }

        var tenantName = await db.Tenants
            .Where(t => t.Id == account.TenantId)
            .Select(t => t.Name)
            .SingleAsync(cancellationToken);

        return sessions.ContinueSession(account, tenantName, rotation.NewToken);
    }
}
