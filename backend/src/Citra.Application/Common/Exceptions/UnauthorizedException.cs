namespace Citra.Application.Common.Exceptions;

/// <summary>
/// Thrown when credentials or a token are not accepted (wrong password, locked account, invalid refresh token).
/// Mapped to HTTP 401. The message is shown to the user, so it must not reveal whether an account exists.
/// </summary>
public sealed class UnauthorizedException(string message) : Exception(message);
