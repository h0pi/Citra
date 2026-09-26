using Citra.Application.Features.Auth.Register;
using FluentValidation;

namespace Citra.Application.Features.Auth.Login;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(RegisterCommandValidator.EmailMaxLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(RegisterCommandValidator.PasswordMaxLength);
    }
}
