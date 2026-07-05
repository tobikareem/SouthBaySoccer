using FluentValidation;

namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Validates Pickup Pal phone sign-in requests.
/// </summary>
public sealed class SignInByPhoneCommandValidator : AbstractValidator<SignInByPhoneCommand>
{
    /// <summary>Initializes a new instance of the <see cref="SignInByPhoneCommandValidator"/> class.</summary>
    public SignInByPhoneCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(value => NormalizeDigits(value).Length is >= 8 and <= 15)
            .WithMessage("Phone number is invalid.");
    }

    internal static string NormalizeDigits(string value) =>
        new(value.Where(char.IsDigit).ToArray());
}
