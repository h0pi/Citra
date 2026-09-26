namespace Citra.Infrastructure.Identity;

/// <summary>
/// One refresh token in the database. Only its SHA-256 hash is stored, so a database leak exposes no usable tokens.
/// Tokens from the same login share a <see cref="FamilyId"/>; each rotation retires the old token and links the new one.
/// </summary>
public sealed class RefreshToken
{
    public const int TokenHashLength = 64; // SHA-256 as hex

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    /// <summary>All tokens descending from one login. Reuse of any retired token revokes the whole family.</summary>
    public Guid FamilyId { get; init; }

    public string TokenHash { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Set when the token was used (rotated), logged out, or revoked because of suspected theft.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>The token that replaced this one during rotation (null if revoked without replacement).</summary>
    public Guid? ReplacedByTokenId { get; set; }
}
