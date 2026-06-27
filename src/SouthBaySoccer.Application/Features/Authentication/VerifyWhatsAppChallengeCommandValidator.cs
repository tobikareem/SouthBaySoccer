using FluentValidation;

namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Validates WhatsApp/Pickup Pal challenge verification at the Application boundary.
/// </summary>
public sealed class VerifyWhatsAppChallengeCommandValidator : AbstractValidator<VerifyWhatsAppChallengeCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyWhatsAppChallengeCommandValidator"/> class.
    /// </summary>
    public VerifyWhatsAppChallengeCommandValidator()
    {
        RuleFor(x => x.ChallengeToken)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MinimumLength(24)
            .MaximumLength(2048)
            .Must(NotContainWhitespace)
            .WithMessage("Challenge token is invalid.");

        RuleFor(x => x.CallbackUri)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(512)
            .Must(BeAbsoluteUri)
            .WithMessage("Callback URI must be an absolute URI.");
    }

    private static bool BeAbsoluteUri(string callbackUri)
    {
        if (string.IsNullOrWhiteSpace(callbackUri))
        {
            return false;
        }

        return Uri.TryCreate(callbackUri, UriKind.Absolute, out var uri) &&
               !string.IsNullOrWhiteSpace(uri.Scheme);
    }

    private static bool NotContainWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.All(character => !char.IsWhiteSpace(character));
    }
}
