using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Auth.Demo;

/// <summary>Which company is the public demo (section "Demo"); none = the demo is off.</summary>
public interface IDemoSettings
{
    /// <summary>The demo company's slug, e.g. "northwind-outdoor"; empty when this server has no demo.</summary>
    string? TenantSlug { get; }
}

/// <summary>
/// "Try demo": logs a visitor in, without a password, as a new read-only demo user of the demo company. Every visit
/// gets its own user, so visitors never see each other's chats (the nightly reset removes them). The Demo role can
/// look at and chat with everything, but change nothing.
/// </summary>
public sealed record DemoLoginCommand : IRequest<AuthResponse>;

internal sealed class DemoLoginCommandHandler(
    IApplicationDbContext db,
    IIdentityService identity,
    IDemoSettings settings,
    AuthSessionIssuer sessions) : IRequestHandler<DemoLoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(DemoLoginCommand request, CancellationToken cancellationToken)
    {
        var tenant = string.IsNullOrWhiteSpace(settings.TenantSlug)
            ? null
            : await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Slug == settings.TenantSlug, cancellationToken);
        if (tenant is null)
        {
            throw new NotFoundException("Demo", settings.TenantSlug ?? "none");
        }

        // Nobody ever logs in with this password: the demo login needs none.
        var email = $"demo-{Guid.NewGuid():N}@{tenant.Slug}.demo";
        var created = await identity.CreateUserAsync(
            new NewUser(tenant.Id, email, "Demo visitor", UserRole.Demo), $"Demo-{Guid.NewGuid():N}!", cancellationToken);
        var account = await identity.FindByIdAsync(
            created.UserId ?? throw new InvalidOperationException("The demo user could not be created."), cancellationToken);

        return await sessions.StartSessionAsync(account!, tenant.Name, cancellationToken);
    }
}
