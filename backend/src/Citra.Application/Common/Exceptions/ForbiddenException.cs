namespace Citra.Application.Common.Exceptions;

/// <summary>
/// Thrown when the current user is authenticated but not allowed to perform the action
/// (e.g. the read-only demo account tries to delete a document). Mapped to HTTP 403.
/// </summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException()
        : base("You do not have permission to perform this action.")
    {
    }

    public ForbiddenException(string message)
        : base(message)
    {
    }
}
