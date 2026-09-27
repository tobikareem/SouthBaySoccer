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

    /// <summary>Deletes the caller's N9ja Bay account. The Pickup Pal account is never touched.</summary>
    Task DeleteMyAccountAsync(CancellationToken cancellationToken);
}
