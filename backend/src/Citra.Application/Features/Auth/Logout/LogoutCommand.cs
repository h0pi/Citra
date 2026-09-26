using Citra.Application.Common.Security;
using Citra.Application.Features.Auth.Refresh;
using FluentValidation;
using MediatR;

namespace Citra.Application.Features.Auth.Logout;

/// <summary>
/// Ends the login session on the server: the refresh token (and its whole session) stops working.
/// Without this, "logout" would only forget the tokens in the browser while the refresh token stayed valid.
/// </summary>
public sealed record LogoutCommand(string RefreshToken) : IRequest;

internal sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(RefreshTokenCommandValidator.RefreshTokenMaxLength);
    }
}

internal sealed class LogoutCommandHandler(IRefreshTokenService refreshTokens) : IRequestHandler<LogoutCommand>
{
    // Unknown tokens are ignored: logging out twice (or with an old token) is not an error.
    public Task Handle(LogoutCommand request, CancellationToken cancellationToken) =>
        refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);
}
