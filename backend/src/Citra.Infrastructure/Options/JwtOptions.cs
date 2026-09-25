using System.ComponentModel.DataAnnotations;

namespace Citra.Infrastructure.Options;

/// <summary>Settings for issuing and validating login tokens. Section: "Jwt". Used by the API only.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>HMAC-SHA256 key; at least 32 characters (256 bits). Stored in User Secrets / env vars, never in appsettings.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 1_440)]
    public int AccessTokenMinutes { get; init; }

    [Range(1, 365)]
    public int RefreshTokenDays { get; init; }
}
