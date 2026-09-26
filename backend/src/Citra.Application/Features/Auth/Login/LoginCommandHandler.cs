using Citra.Application.Common.Exceptions;
using Citra.Application.Common.Persistence;
using Citra.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Citra.Application.Features.Auth.Login;

internal sealed class LoginCommandHandler(
    IApplicationDbContext db,
    IIdentityService identity,
    AuthSessionIssuer sessions,
    TimeProvider timeProvider) : IRequestHandler<LoginCommand, AuthResponse>
{
    /// <summary>Same message for unknown email and wrong password, so it can't be used to discover accounts.</summary>
    public const string InvalidCredentialsMessage = "Invalid email or password.";

    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var result = await identity.CheckCredentialsAsync(email, request.Password, cancellationToken);

        switch (result.Status)
        {
            case CredentialCheckStatus.LockedOut:
                throw new UnauthorizedException(LockedOutMessage(result.LockedUntil));

            case CredentialCheckStatus.InvalidCredentials:
                throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        var account = result.Account!;
        var tenantName = await db.Tenants
            .Where(t => t.Id == account.TenantId)
            .Select(t => t.Name)
            .SingleAsync(cancellationToken);

        return await sessions.StartSessionAsync(account, tenantName, cancellationToken);
    }

    private string LockedOutMessage(DateTimeOffset? lockedUntil)
    {
        var minutes = lockedUntil is null
            ? 15
            : Math.Max(1, (int)Math.Ceiling((lockedUntil.Value - timeProvider.GetUtcNow()).TotalMinutes));

        return $"Too many failed attempts. Please try again in {minutes} minute{(minutes == 1 ? "" : "s")}.";
    }
}
