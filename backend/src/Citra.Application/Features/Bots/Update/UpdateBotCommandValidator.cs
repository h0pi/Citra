using Citra.Domain.Bots;
using FluentValidation;

namespace Citra.Application.Features.Bots.Update;

internal sealed class UpdateBotCommandValidator : AbstractValidator<UpdateBotCommand>
{
    public UpdateBotCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Bot.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Bot.DescriptionMaxLength);
        RuleFor(x => x.SystemPrompt).MaximumLength(Bot.SystemPromptMaxLength);
        RuleFor(x => x.WelcomeMessage).NotEmpty().MaximumLength(Bot.WelcomeMessageMaxLength);

        RuleFor(x => x.Model)
            .Must(m => BotModels.IsSupported(m!))
            .When(x => !string.IsNullOrWhiteSpace(x.Model))
            .WithMessage($"Model must be one of: {string.Join(", ", BotModels.Supported)}.");

        RuleFor(x => x.Temperature).InclusiveBetween(0.0, 1.0);

        RuleFor(x => x.WidgetColor)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Matches("^#[0-9A-Fa-f]{6}$")
            .WithMessage("Widget color must be a hex color like #6366F1.");

        RuleFor(x => x.StarterQuestions)
            .NotNull()
            .Must(q => q.Count(s => !string.IsNullOrWhiteSpace(s)) <= Bot.MaxStarterQuestions)
            .WithMessage($"At most {Bot.MaxStarterQuestions} starter questions.");
        RuleForEach(x => x.StarterQuestions).MaximumLength(Bot.StarterQuestionMaxLength);
    }
}
