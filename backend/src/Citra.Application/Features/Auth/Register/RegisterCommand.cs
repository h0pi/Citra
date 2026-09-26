using MediatR;

namespace Citra.Application.Features.Auth.Register;

/// <summary>Sign-up: creates a new company (tenant) and its owner account, then logs the owner in.</summary>
public sealed record RegisterCommand(
    string CompanyName,
    string DisplayName,
    string Email,
    string Password) : IRequest<AuthResponse>;
