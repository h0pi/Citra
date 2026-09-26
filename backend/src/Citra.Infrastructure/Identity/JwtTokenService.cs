using Citra.Application.Common.Security;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Citra.Infrastructure.Identity;

/// <summary>
/// Creates signed JWT access tokens (HMAC-SHA256). Short-lived by design (Jwt:AccessTokenMinutes, default 60);
/// the refresh token (C-074) gets a new one without asking for the password again.
/// </summary>
internal sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(TokenUser user)
    {
        var jwt = options.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(jwt.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                JwtTokenValidation.CreateSigningKey(jwt),
                JwtTokenValidation.SigningAlgorithm),
            Claims = new Dictionary<string, object>
            {
                [CitraClaimTypes.UserId] = user.UserId.ToString(),
                [CitraClaimTypes.Email] = user.Email,
                [CitraClaimTypes.DisplayName] = user.DisplayName,
                [CitraClaimTypes.Role] = user.Role.ToString(),
                [CitraClaimTypes.TenantId] = user.TenantId.ToString(),
                // Unique token id: lets a specific token be identified (and blocked) later if needed.
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
