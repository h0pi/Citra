using FluentValidation;

namespace Citra.Application.Features.Documents.List;

internal sealed class GetDocumentsQueryValidator : AbstractValidator<GetDocumentsQuery>
{
    public GetDocumentsQueryValidator()
    {
        RuleFor(x => x.BotId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetDocumentsQuery.MaxPageSize);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Search).MaximumLength(GetDocumentsQuery.SearchMaxLength);
    }
}
