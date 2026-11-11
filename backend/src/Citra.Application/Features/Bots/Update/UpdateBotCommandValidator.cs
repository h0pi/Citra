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

        RuleFor(x => x.AllowedDomains)
            .NotNull()
            .Must(d => d.Count(s => !string.IsNullOrWhiteSpace(s)) <= Bot.MaxAllowedDomains)
            .WithMessage($"At most {Bot.MaxAllowedDomains} websites.");
        // Only the domain ("acme.com"), as the Origin check compares hosts; not "https://acme.com/page".
        RuleForEach(x => x.AllowedDomains)
            .Matches(@"^\s*([A-Za-z0-9-]+\.)*[A-Za-z0-9-]+\s*$|^\s*$")
            .WithMessage("Use domain names like acme.com, without https:// or a path.");

        RuleFor(x => x.DailyMessageLimit).GreaterThanOrEqualTo(0);
    }
}
