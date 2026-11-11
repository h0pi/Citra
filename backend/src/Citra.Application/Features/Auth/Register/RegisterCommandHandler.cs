using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using Citra.Domain.Tenants;
using Citra.Domain.Users;
using MediatR;

namespace Citra.Application.Features.Auth.Register;

/// <summary>Whether new companies may sign up (section "Registration").</summary>
public interface IRegistrationSettings
{
    bool Enabled { get; }
}

internal sealed class RegisterCommandHandler(
    IApplicationDbContext db,
    IIdentityService identity,
    IRegistrationSettings settings,
    AuthSessionIssuer sessions) : IRequestHandler<RegisterCommand, AuthResponse>
{
    public const string ClosedMessage = "Sign-up is closed on this site. Use \"Try the demo\" on the login page to look around.";

    public async Task<AuthResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            throw new ForbiddenException(ClosedMessage);
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var displayName = request.DisplayName.Trim();

        if (await identity.IsEmailTakenAsync(email, cancellationToken))
        {
            throw new ValidationException(nameof(RegisterCommand.Email), "An account with this email already exists.");
        }

        var tenant = Tenant.Create(request.CompanyName);
        tenant.ChangeSlug(await UniqueSlug.FindAsync(db.Tenants.Select(t => t.Slug), tenant.Slug, cancellationToken));
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
}
