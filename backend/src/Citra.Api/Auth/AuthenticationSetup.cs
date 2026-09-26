using Citra.Application.Common.Security;
using Citra.Infrastructure.Identity;
using Citra.Infrastructure.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Citra.Api.Auth;

internal static class AuthenticationSetup
{
    /// <summary>
    /// Every request with "Authorization: Bearer &lt;token&gt;" is checked with the same rules the token
    /// was created with (JwtTokenValidation). Endpoints marked [Authorize] reject missing/invalid tokens with 401.
    /// </summary>
    public static IServiceCollection AddCitraAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured through options so it uses the validated JwtOptions (User Secrets / env vars).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                // Keep short claim names ("sub", "role", "tenant_id") instead of long XML-style URIs.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = JwtTokenValidation.CreateParameters(jwt.Value);
            });

        services.AddAuthorization();

        // In the API, "who is the current user" comes from the verified token of the current request.
        // Replaces Infrastructure's default "no user" (used by the Worker).
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser, HttpCurrentUser>());

        return services;
    }
}
