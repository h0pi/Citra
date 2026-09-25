using FluentValidation.Results;

namespace Citra.Application.Common.Exceptions;

/// <summary>
/// Thrown when a request fails validation. Errors are grouped by property name,
/// e.g. { "Name": ["'Name' must not be empty."] }.
/// </summary>
public sealed class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : base("One or more validation errors occurred.")
    {
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.Distinct().ToArray());
    }
}
