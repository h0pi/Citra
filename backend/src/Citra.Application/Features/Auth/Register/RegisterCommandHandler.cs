using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Common;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Auth.Register;

internal sealed class RegisterCommandHandler(
    IApplicationDbContext db,
    IIdentityService identity,
    AuthSessionIssuer sessions) : IRequestHandler<RegisterCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var displayName = request.DisplayName.Trim();

        if (await identity.IsEmailTakenAsync(email, cancellationToken))
        {
            throw new ValidationException(nameof(RegisterCommand.Email), "An account with this email already exists.");
        }

        var tenant = Tenant.Create(request.CompanyName);
        tenant.ChangeSlug(await UniqueSlugAsync(tenant.Slug, cancellationToken));
        db.Tenants.Add(tenant);

        // Saves the tenant (tracked above) and the user together: if the user is rejected, no tenant is left behind.
        var created = await identity.CreateUserAsync(
            new NewUser(tenant.Id, email, displayName, UserRole.Owner),
            request.Password,
            cancellationToken);

        if (!created.Succeeded)
        {
            throw new ValidationException(created.Errors);
        }

        var account = new UserAccount(created.UserId!.Value, tenant.Id, email, displayName, UserRole.Owner);
        return await sessions.StartSessionAsync(account, tenant.Name, cancellationToken);
    }

    /// <summary>"acme" if free, otherwise "acme-2", "acme-3", ...</summary>
    private async Task<string> UniqueSlugAsync(string baseSlug, CancellationToken cancellationToken)
    {
        var taken = await db.Tenants
            .Where(t => t.Slug == baseSlug || t.Slug.StartsWith(baseSlug + "-"))
            .Select(t => t.Slug)
            .ToListAsync(cancellationToken);

        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        var suffix = 2;
        while (taken.Contains($"{baseSlug}-{suffix}"))
        {
            suffix++;
        }

        var candidate = $"{baseSlug}-{suffix}";
        return candidate.Length <= Slug.MaxLength ? candidate : $"{baseSlug[..(Slug.MaxLength - 8)]}-{suffix}";
    }
}
