using Citra.Application.Common.Security;
using Citra.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.Infrastructure.Identity;

public static class IdentityRegistration
{
    public const int PasswordMinLength = 8;
    public const int MaxFailedLoginAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// ASP.NET Identity (user store, password hashing/validation, lockout) plus the token services.
    /// Called from AddInfrastructure. JwtOptions are registered and validated by the API only.
    /// </summary>
    public static IServiceCollection AddCitraIdentity(this IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Length over complexity (NIST guidance): 8+ chars with a letter and a digit,
                // no forced symbols/uppercase — those mostly lead to passwords written on sticky notes.
                options.Password.RequiredLength = PasswordMinLength;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 4;

                options.User.RequireUniqueEmail = true;

                // Slows down password guessing: 5 wrong attempts → locked for 15 minutes.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = MaxFailedLoginAttempts;
                options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;
            })
            .AddEntityFrameworkStores<CitraDbContext>();

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IIdentityService, IdentityService>();

        // No web request → no user. The API replaces this with the request-based implementation.
        services.AddScoped<ICurrentUser, NoCurrentUser>();

        return services;
    }
}
