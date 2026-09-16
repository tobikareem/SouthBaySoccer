using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Functions.Onboarding;

/// <summary>Maps the sign-up, terms, and account-deletion contracts onto Application commands.</summary>
public interface IOnboardingWorkflow
{
    Task<RegistrationTokenValidationResponse> ValidateRegistrationTokenAsync(
        ValidateRegistrationTokenRequest request,
        CancellationToken cancellationToken);

    Task<CheckEmailAvailabilityResponse> CheckEmailAvailabilityAsync(
        CheckEmailAvailabilityRequest request,
        CancellationToken cancellationToken);

    Task<RegistrationCompletedResponse> RegisterWithWhatsAppAsync(
        RegisterWithWhatsAppRequest request,
        CancellationToken cancellationToken);

    TermsVersionResponse GetCurrentTermsVersion();

    /// <param name="alsoDeletePickupPalAccount">When true the player explicitly opted to remove their Pickup Pal account too.</param>
    Task DeleteMyAccountAsync(bool alsoDeletePickupPalAccount, CancellationToken cancellationToken);
}
