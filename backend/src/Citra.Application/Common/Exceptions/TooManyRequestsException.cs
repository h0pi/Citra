namespace Citra.Application.Common.Exceptions;

/// <summary>Someone sends requests faster than allowed. Mapped to HTTP 429; waiting a little helps.</summary>
public sealed class TooManyRequestsException(string message) : Exception(message);
