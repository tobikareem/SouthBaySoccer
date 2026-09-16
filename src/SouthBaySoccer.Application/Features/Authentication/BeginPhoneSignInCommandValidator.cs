using FluentValidation;

namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Validates Pickup Pal phone sign-in requests.
/// </summary>
public sealed class BeginPhoneSignInCommandValidator : AbstractValidator<BeginPhoneSignInCommand>
{
    /// <summary>Initializes a new instance of the <see cref="BeginPhoneSignInCommandValidator"/> class.</summary>
    public BeginPhoneSignInCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(value => NormalizeDigits(value).Length is >= 8 and <= 15)
            .WithMessage("Phone number is invalid.");
    }

    /// <summary>
    /// Normalizes user-typed phone input to the digits Pickup Pal keys on. Public so the Functions
    /// rate limiter can key per phone on the same value the lookup uses.
    /// </summary>
    public static string NormalizeDigits(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());

        // Pickup Pal keys US users by their 11-digit "1XXXXXXXXXX" number. Players routinely type
        // the 10-digit form without the country code, which would miss the lookup, so assume US and
        // prepend the "1". Longer international numbers already carry their code and pass through.
        return digits.Length == 10 ? $"1{digits}" : digits;
    }
}
