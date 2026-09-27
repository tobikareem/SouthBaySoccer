using FluentValidation;

namespace SouthBaySoccer.Application.Features.Onboarding;

/// <summary>
/// Pre-flight email uniqueness check so a duplicate never spends the registration token
/// (Pickup Pal redeems the token before its own email check).
/// </summary>
/// <param name="Email">The email the player intends to register with.</param>
public sealed record CheckEmailAvailabilityCommand(string Email);

/// <summary>Validates <see cref="CheckEmailAvailabilityCommand"/>.</summary>
public sealed class CheckEmailAvailabilityCommandValidator : AbstractValidator<CheckEmailAvailabilityCommand>
{
    /// <summary>Initializes a new instance of the <see cref="CheckEmailAvailabilityCommandValidator"/> class.</summary>
    public CheckEmailAvailabilityCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(RegistrationRules.EmailMaxLength).EmailAddress();
    }
}

/// <summary>Handles <see cref="CheckEmailAvailabilityCommand"/>.</summary>
public sealed class CheckEmailAvailabilityCommandHandler(
    IValidator<CheckEmailAvailabilityCommand> validator,
    IPickupPalOnboardingClient onboardingClient)
{
    /// <summary>Returns <see langword="true"/> when Pickup Pal has no account for the email.</summary>
    public async Task<bool> HandleAsync(
        CheckEmailAvailabilityCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        return await onboardingClient.IsEmailAvailableAsync(command.Email.Trim(), cancellationToken);
    }
}
