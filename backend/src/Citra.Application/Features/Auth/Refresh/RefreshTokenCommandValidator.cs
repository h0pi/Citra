using FluentValidation;

namespace Citra.Application.Features.Auth.Refresh;

internal sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    /// <summary>Our tokens are 86 characters; anything far longer is not a Citra token.</summary>
    public const int RefreshTokenMaxLength = 200;

    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(RefreshTokenMaxLength);
    }
}
