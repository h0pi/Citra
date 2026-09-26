using MediatR;

namespace Citra.Application.Features.Auth.Login;

/// <summary>Email + password login. Returns an access token, a refresh token and the user.</summary>
public sealed record LoginCommand(string Email, string Password) : IRequest<AuthResponse>;
