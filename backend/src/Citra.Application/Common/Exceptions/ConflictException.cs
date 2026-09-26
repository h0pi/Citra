namespace Citra.Application.Common.Exceptions;

/// <summary>
/// The request is valid, but the current state does not allow it right now (e.g. reprocessing a document
/// that is already being processed). Mapped to HTTP 409; trying again later may succeed.
/// </summary>
public sealed class ConflictException(string message) : Exception(message);
