using System.Text;
using Citra.Application.Common.Security;
using Citra.Infrastructure.Options;
using Microsoft.IdentityModel.Tokens;

namespace Citra.Infrastructure.Identity;

/// <summary>
/// The single definition of how Citra signs and validates access tokens, shared by the token
/// service (creating) and the API's authentication middleware (checking), so the two never drift apart.
/// </summary>
public static class JwtTokenValidation
{
    public const string SigningAlgorithm = SecurityAlgorithms.HmacSha256;

    /// <summary>Small tolerance for clock differences between servers (default would be 5 minutes).</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public static SymmetricSecurityKey CreateSigningKey(JwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));

    public static TokenValidationParameters CreateParameters(JwtOptions options) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = CreateSigningKey(options),
            ValidAlgorithms = [SigningAlgorithm],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
            NameClaimType = CitraClaimTypes.DisplayName,
            RoleClaimType = CitraClaimTypes.Role,
        };
}
