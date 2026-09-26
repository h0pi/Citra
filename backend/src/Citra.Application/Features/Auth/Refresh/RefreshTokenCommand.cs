using MediatR;

namespace Citra.Application.Features.Auth.Refresh;

/// <summary>Exchanges a refresh token for a new access + refresh token pair (the old refresh token stops working).</summary>
public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResponse>;
