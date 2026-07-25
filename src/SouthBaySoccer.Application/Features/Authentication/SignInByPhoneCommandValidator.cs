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

    internal static string NormalizeDigits(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());

        // Pickup Pal keys US users by their 11-digit "1XXXXXXXXXX" number. Players routinely type
        // the 10-digit form without the country code, which would miss the lookup, so assume US and
        // prepend the "1". Longer international numbers already carry their code and pass through.
        return digits.Length == 10 ? $"1{digits}" : digits;
    }
}
