using Citra.Domain.Users;

namespace Citra.Application.Common.Security;

/// <summary>Data for a new login account.</summary>
public sealed record NewUser(Guid TenantId, string Email, string DisplayName, UserRole Role);

/// <summary>
/// Outcome of creating a user. On failure, <see cref="Errors"/> is keyed by field ("Email", "Password"),
/// ready to become a 400 response with per-field messages.
/// </summary>
public sealed record CreateUserResult(Guid? UserId, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => UserId is not null;
}

/// <summary>A user account's details as the application needs them.</summary>
public sealed record UserAccount(Guid UserId, Guid TenantId, string Email, string DisplayName, UserRole Role);

public enum CredentialCheckStatus
{
    Succeeded,

    /// <summary>Unknown email or wrong password — deliberately indistinguishable.</summary>
    InvalidCredentials,

    /// <summary>Too many failed attempts; login is blocked until <see cref="CredentialCheckResult.LockedUntil"/>.</summary>
    LockedOut,
}

public sealed record CredentialCheckResult(
    CredentialCheckStatus Status,
    UserAccount? Account = null,
    DateTimeOffset? LockedUntil = null);

/// <summary>
/// User accounts, as seen by the Application layer. Hides ASP.NET Identity (Infrastructure)
/// so handlers stay testable and free of framework types.
/// </summary>
public interface IIdentityService
{
    Task<bool> IsEmailTakenAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the user and saves all pending changes of the current unit of work in the same transaction
    /// (e.g. a new tenant added just before) — so either everything is saved or nothing is.
    /// </summary>
    Task<CreateUserResult> CreateUserAsync(NewUser user, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Checks email + password with lockout protection: failures are counted, the account locks after too many,
    /// and a success resets the counter and records the login time. Timing does not reveal whether the email exists.
    /// </summary>
    Task<CredentialCheckResult> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A company's users with this role, created before <paramref name="createdBefore"/>.</summary>
    Task<IReadOnlyList<Guid>> FindUserIdsAsync(
        Guid tenantId, UserRole role, DateTimeOffset createdBefore, CancellationToken cancellationToken);

    /// <summary>Deletes the users with their logins (refresh tokens, roles).</summary>
    Task DeleteUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}
