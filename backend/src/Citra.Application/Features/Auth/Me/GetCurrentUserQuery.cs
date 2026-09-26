using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Auth.Me;

/// <summary>"Who am I?" — the logged-in user, loaded fresh (renames show immediately, not only after re-login).</summary>
public sealed record GetCurrentUserQuery : IRequest<CurrentUserDto>;

internal sealed class GetCurrentUserQueryHandler(
    ICurrentUser currentUser,
    IIdentityService identity,
    IApplicationDbContext db) : IRequestHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public async Task<CurrentUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        // The token is valid, but the account may have been deleted since it was issued.
        var account = await identity.FindByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException("Your account no longer exists.");

        var tenantName = await db.Tenants
            .Where(t => t.Id == account.TenantId)
            .Select(t => t.Name)
            .SingleAsync(cancellationToken);

        return new CurrentUserDto(
            account.UserId,
            account.Email,
            account.DisplayName,
            account.Role,
            account.TenantId,
            tenantName);
    }
}
