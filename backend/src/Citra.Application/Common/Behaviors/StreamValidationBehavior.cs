using System.Runtime.CompilerServices;
using FluentValidation;
using MediatR;
using ValidationException = Citra.Application.Common.Exceptions.ValidationException;

namespace Citra.Application.Common.Behaviors;

/// <summary>
/// <see cref="ValidationBehavior{TRequest,TResponse}"/> for streaming requests (e.g. a chat answer written word by
/// word): the validators run before the first item, so an invalid request never starts streaming.
/// </summary>
public sealed class StreamValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));
            var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
            if (failures.Count > 0)
            {
                throw new ValidationException(failures);
            }
        }

        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }
}
