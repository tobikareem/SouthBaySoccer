using FluentValidation;
using SouthBaySoccer.Application.Features.Authentication;

namespace SouthBaySoccer.Application.Features.Onboarding;

/// <summary>Checks a registration token before the details form is shown. Never spends the token.</summary>
/// <param name="Token">The single-use registration token from the app link.</param>
public sealed record ValidateRegistrationTokenCommand(string Token);

/// <summary>What the details form shows for a valid token.</summary>
/// <param name="PhoneMasked">The masked phone the token was minted for.</param>
public sealed record RegistrationTokenValidated(string PhoneMasked);

/// <summary>Validates <see cref="ValidateRegistrationTokenCommand"/>.</summary>
public sealed class ValidateRegistrationTokenCommandValidator : AbstractValidator<ValidateRegistrationTokenCommand>
{
    /// <summary>Initializes a new instance of the <see cref="ValidateRegistrationTokenCommandValidator"/> class.</summary>
    public ValidateRegistrationTokenCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(OnboardingTokenRules.MaxLength);
    }
}

/// <summary>Shared token shape rules.</summary>
internal static class OnboardingTokenRules
{
    /// <summary>Pickup Pal tokens are UUIDs; the cap only guards against abusive payloads.</summary>
    public const int MaxLength = 256;
}

/// <summary>Handles <see cref="ValidateRegistrationTokenCommand"/>.</summary>
public sealed class ValidateRegistrationTokenCommandHandler(
    IValidator<ValidateRegistrationTokenCommand> validator,
    IPickupPalOnboardingClient onboardingClient,
    IPickupPalUserClient pickupPalUserClient)
{
    /// <summary>Validates the token with Pickup Pal and confirms the phone is not already registered.</summary>
    /// <exception cref="OnboardingTokenException">The token is expired, invalid, or the phone already has an account.</exception>
    public async Task<RegistrationTokenValidated> HandleAsync(
        ValidateRegistrationTokenCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var validation = await onboardingClient.ValidateRegistrationTokenAsync(command.Token.Trim(), cancellationToken);
        switch (validation.Status)
        {
            case RegistrationTokenStatus.Expired:
                throw new OnboardingTokenException(OnboardingTokenFailure.Expired);
            case RegistrationTokenStatus.Invalid:
                throw new OnboardingTokenException(OnboardingTokenFailure.Invalid);
        }

        if (string.IsNullOrWhiteSpace(validation.PhoneNumberDigits))
        {
            // Registration needs the token's phone (Pickup Pal copies it onto the account); a
            // token without one cannot complete, so report it the same way the register step would.
            throw new OnboardingTokenException(OnboardingTokenFailure.Invalid);
        }

        // The bot refuses to mint a token for a number that already has an account, but a race
        // (web sign-up between the DM and the app link) still leaves this check worth one lookup:
        // registering would burn the token and fail anyway.
        var existing = await pickupPalUserClient.FindByPhoneAsync(validation.PhoneNumberDigits, cancellationToken);
        if (existing is not null)
        {
            throw new OnboardingTokenException(OnboardingTokenFailure.AlreadyRegistered);
        }

        return new RegistrationTokenValidated(OnboardingPhone.Mask(validation.PhoneNumberDigits));
    }
}
