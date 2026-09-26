using Citra.Application.Common.Security;
using Microsoft.AspNetCore.Identity;

namespace Citra.Infrastructure.Identity;

/// <summary>ASP.NET Identity behind the Application layer's <see cref="IIdentityService"/>.</summary>
internal sealed class IdentityService(UserManager<ApplicationUser> userManager, TimeProvider timeProvider)
    : IIdentityService
{
    private const string EmailField = "Email";
    private const string PasswordField = "Password";
    private const string GeneralField = "Account";

    /// <summary>
    /// A real password hash nobody knows the password for. Checking it for unknown emails makes a
    /// "no such user" login take as long as a "wrong password" login, so timing can't reveal which emails exist.
    /// </summary>
    private static readonly Lazy<string> TimingEqualizerHash = new(() =>
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), Guid.NewGuid().ToString()));

    public async Task<bool> IsEmailTakenAsync(string email, CancellationToken cancellationToken) =>
        await userManager.FindByEmailAsync(email) is not null;

    public async Task<CredentialCheckResult> CheckCredentialsAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            userManager.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), TimingEqualizerHash.Value, password);
            return new CredentialCheckResult(CredentialCheckStatus.InvalidCredentials);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return new CredentialCheckResult(CredentialCheckStatus.LockedOut, LockedUntil: user.LockoutEnd);
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return await userManager.IsLockedOutAsync(user)
                ? new CredentialCheckResult(CredentialCheckStatus.LockedOut, LockedUntil: user.LockoutEnd)
                : new CredentialCheckResult(CredentialCheckStatus.InvalidCredentials);
        }

        await userManager.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = timeProvider.GetUtcNow();
        await userManager.UpdateAsync(user);

        return new CredentialCheckResult(CredentialCheckStatus.Succeeded, ToAccount(user));
    }

    public async Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : ToAccount(user);
    }

    private static UserAccount ToAccount(ApplicationUser user) =>
        new(user.Id, user.TenantId, user.Email ?? string.Empty, user.DisplayName, user.Role);

    public async Task<CreateUserResult> CreateUserAsync(NewUser user, string password, CancellationToken cancellationToken)
    {
        var account = new ApplicationUser
        {
            TenantId = user.TenantId,
            UserName = user.Email,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role,
        };

        // UserManager saves through the shared DbContext, so any other pending changes
        // (e.g. the new tenant) are committed in the same transaction as the user.
        var result = await userManager.CreateAsync(account, password);

        return result.Succeeded
            ? new CreateUserResult(account.Id, new Dictionary<string, string[]>())
            : new CreateUserResult(null, ToFieldErrors(result.Errors));
    }

    /// <summary>Groups Identity's error codes by the form field they belong to.</summary>
    private static Dictionary<string, string[]> ToFieldErrors(IEnumerable<IdentityError> errors) =>
        errors
            .GroupBy(e => e.Code switch
            {
                _ when e.Code.StartsWith(PasswordField, StringComparison.Ordinal) => PasswordField,
                "DuplicateEmail" or "InvalidEmail" or "DuplicateUserName" or "InvalidUserName" => EmailField,
                _ => GeneralField,
            })
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Code is "DuplicateEmail" or "DuplicateUserName"
                        ? "An account with this email already exists."
                        : e.Description)
                    .Distinct()
                    .ToArray());
}
