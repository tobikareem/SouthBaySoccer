using FluentValidation;

namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Validates WhatsApp/Pickup Pal challenge requests at the Application boundary.
/// </summary>
public sealed class RequestWhatsAppChallengeCommandValidator : AbstractValidator<RequestWhatsAppChallengeCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestWhatsAppChallengeCommandValidator"/> class.
    /// </summary>
    public RequestWhatsAppChallengeCommandValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(16)
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage("Phone number must be in E.164 format.");

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
}
