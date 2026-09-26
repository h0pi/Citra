using System.Security.Cryptography;
using System.Text;
using Citra.Application.Common.Security;
using Citra.Infrastructure.Options;
using Citra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Citra.Infrastructure.Identity;

internal sealed class RefreshTokenService(
    CitraDbContext db,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider,
    ILogger<RefreshTokenService> logger) : IRefreshTokenService
{
    private const int TokenBytes = 64;

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (entity, issued) = Create(userId, familyId: Guid.CreateVersion7());
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return issued;
    }

    public async Task<RefreshTokenRotation?> RotateAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var hash = Hash(token);
        var current = await db.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (current is null || current.ExpiresAt <= now)
        {
            return null;
        }

        if (current.RevokedAt is not null)
        {
            // Only a token that was already *rotated* (has a successor) signals theft. A token ended by
            // logout, or already revoked with its session, is simply rejected — no alarm, nothing to revoke.
            if (current.ReplacedByTokenId is not null)
            {
                await HandleReuseAsync(current, now, cancellationToken);
            }

            return null;
        }

        var (replacement, issued) = Create(current.UserId, current.FamilyId);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Atomic "retire only if still active": if two requests race with the same token,
        // exactly one gets rowsAffected = 1; the other is treated as reuse.
        var retired = await db.RefreshTokens
            .Where(t => t.Id == current.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.ReplacedByTokenId, replacement.Id),
                cancellationToken);

        if (retired == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            await HandleReuseAsync(current, now, cancellationToken);
            return null;
        }

        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RefreshTokenRotation(current.UserId, issued);
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var hash = Hash(token);
        var familyId = await db.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);

        if (familyId is not null)
        {
            await RevokeFamilyAsync(familyId.Value, timeProvider.GetUtcNow(), cancellationToken);
        }
    }

    /// <summary>A retired token came back: someone kept a copy. End the whole session for everyone holding it.</summary>
    private async Task HandleReuseAsync(RefreshToken reused, DateTimeOffset now, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Refresh token reuse detected for user {UserId} (family {FamilyId}); revoking the session",
            reused.UserId, reused.FamilyId);

        await RevokeFamilyAsync(reused.FamilyId, now, cancellationToken);
    }

    private Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

    private (RefreshToken Entity, IssuedRefreshToken Issued) Create(Guid userId, Guid familyId)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddDays(options.Value.RefreshTokenDays);
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

        var entity = new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };

        return (entity, new IssuedRefreshToken(token, expiresAt));
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
