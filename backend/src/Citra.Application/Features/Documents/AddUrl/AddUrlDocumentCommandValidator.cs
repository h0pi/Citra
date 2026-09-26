using Citra.Application.Common.Web;
using Citra.Domain.Documents;
using FluentValidation;

namespace Citra.Application.Features.Documents.AddUrl;

internal sealed class AddUrlDocumentCommandValidator : AbstractValidator<AddUrlDocumentCommand>
{
    public AddUrlDocumentCommandValidator()
    {
        RuleFor(x => x.BotId).NotEmpty();

        RuleFor(x => x.Url)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Enter a web address.")
            .MaximumLength(Document.SourceUrlMaxLength).WithMessage("This web address is too long.")
            .Must(url => Uri.TryCreate(url.Trim(), UriKind.Absolute, out _))
            .WithMessage("Enter a full web address, e.g. https://example.com/help.")
            .Custom((url, context) =>
            {
                if (UrlSafety.Check(new Uri(url.Trim())) is { } problem)
                {
                    context.AddFailure(problem);
                }
            });
    }
}
