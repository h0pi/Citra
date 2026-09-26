using Citra.Application.Features.Auth;
using Citra.Application.Features.Auth.Login;
using Citra.Application.Features.Auth.Logout;
using Citra.Application.Features.Auth.Me;
using Citra.Application.Features.Auth.Refresh;
using Citra.Application.Features.Auth.Register;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citra.Api.Controllers;

/// <summary>Sign-up and login. Thin: every action just sends a command/query through MediatR.</summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    /// <summary>Creates a new company and its owner account, and logs the owner in.</summary>
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<AuthResponse> Register(RegisterCommand command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);

    /// <summary>Logs in with email and password. Five wrong attempts lock the account for 15 minutes.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<AuthResponse> Login(LoginCommand command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);

    /// <summary>
    /// Swaps a refresh token for a new access + refresh token pair. Each refresh token works once;
    /// reusing an old one ends the whole session (it was probably stolen).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<AuthResponse> Refresh(RefreshTokenCommand command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);

    /// <summary>The logged-in user (requires a valid access token).</summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<CurrentUserDto> Me(CancellationToken cancellationToken) =>
        sender.Send(new GetCurrentUserQuery(), cancellationToken);

    /// <summary>Ends the session on the server: the refresh token stops working. Always succeeds.</summary>
    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(LogoutCommand command, CancellationToken cancellationToken)
    {
        await sender.Send(command, cancellationToken);
        return NoContent();
    }
}
