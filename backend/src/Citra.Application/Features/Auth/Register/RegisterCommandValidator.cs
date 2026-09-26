using Citra.Domain.Tenants;
using FluentValidation;

namespace Citra.Application.Features.Auth.Register;

internal sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public const int EmailMaxLength = 256;
    public const int DisplayNameMaxLength = 100;
    public const int PasswordMaxLength = 128;

    public RegisterCommandValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(Tenant.NameMaxLength);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(DisplayNameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(EmailMaxLength);

        // Detailed password rules (length, digit) are enforced by ASP.NET Identity so they live in one place;
        // this only catches empty and absurdly long input early.
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordMaxLength);
    }
}
