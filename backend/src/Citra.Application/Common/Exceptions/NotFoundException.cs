namespace Citra.Application.Common.Exceptions;

/// <summary>
/// Thrown when a requested entity does not exist (or belongs to another tenant). Mapped to HTTP 404.
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string entityName, object key)
        : base($"{entityName} '{key}' was not found.")
    {
    }
}
