using Citra.Application.Common.Security;
using Citra.Infrastructure.Identity;
using Citra.Infrastructure.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Citra.Api.Auth;

internal static class AuthenticationSetup
{
    /// <summary>Where the SignalR hubs live (see Hubs/): the only place a token may come in the query string.</summary>
    public const string HubsPath = "/hubs";

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

                // Browsers cannot send an Authorization header on WebSockets (or server-sent events), so SignalR
                // puts the token in the address: ?access_token=… . Accepted ONLY for the live-update hubs, never for
                // normal API calls, so tokens don't end up in URLs anywhere else. (Request logs record paths without
                // the query string, so the token is not logged.)
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.HttpContext.Request.Path.StartsWithSegments(HubsPath)
                            && context.Request.Query["access_token"] is { Count: 1 } token
                            && !string.IsNullOrEmpty(token[0]))
                        {
                            context.Token = token[0];
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();

        // In the API, "who is the current user" comes from the verified token of the current request.
        // Replaces Infrastructure's default "no user" (used by the Worker).
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser, HttpCurrentUser>());

        return services;
    }
}
