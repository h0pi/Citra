using Citra.Domain.Bots;
using FluentValidation;

namespace Citra.Application.Features.Bots.Create;

internal sealed class CreateBotCommandValidator : AbstractValidator<CreateBotCommand>
{
    public CreateBotCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Bot.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Bot.DescriptionMaxLength);
    }
}
