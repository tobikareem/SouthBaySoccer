using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Functions.Onboarding;

public sealed class OnboardingWorkflow(
    ValidateRegistrationTokenCommandHandler validateRegistrationTokenHandler,
    CheckEmailAvailabilityCommandHandler checkEmailAvailabilityHandler,
    RegisterWithWhatsAppCommandHandler registerWithWhatsAppHandler,
    DeleteAccountCommandHandler deleteAccountHandler,
    IOnboardingPolicy onboardingPolicy) : IOnboardingWorkflow
{
    public async Task<RegistrationTokenValidationResponse> ValidateRegistrationTokenAsync(
        ValidateRegistrationTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await validateRegistrationTokenHandler.HandleAsync(
            new ValidateRegistrationTokenCommand(request.Token),
            cancellationToken);

        return new RegistrationTokenValidationResponse(result.PhoneMasked);
    }

    public async Task<CheckEmailAvailabilityResponse> CheckEmailAvailabilityAsync(
        CheckEmailAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        var isAvailable = await checkEmailAvailabilityHandler.HandleAsync(
            new CheckEmailAvailabilityCommand(request.Email),
            cancellationToken);

        return new CheckEmailAvailabilityResponse(isAvailable);
    }

    public async Task<RegistrationCompletedResponse> RegisterWithWhatsAppAsync(
        RegisterWithWhatsAppRequest request,
        CancellationToken cancellationToken)
    {
        var result = await registerWithWhatsAppHandler.HandleAsync(
            new RegisterWithWhatsAppCommand(
                request.Token,
                request.FirstName,
                request.LastName,
                request.Email,
                request.Password,
                request.PreferredPosition,
                request.TermsVersion,
                request.TermsAcceptedAtUtc),
            cancellationToken);

        return new RegistrationCompletedResponse(
            new AuthenticationTokensResponse(
                result.Tokens.AccessToken,
                result.Tokens.RefreshToken,
                result.Tokens.AccessTokenExpiresAtUtc),
            result.FirstName,
            result.PhoneMasked,
            result.GroupNames,
            result.HistorySyncPending);
    }

    public TermsVersionResponse GetCurrentTermsVersion() => new(onboardingPolicy.TermsVersion);

    public Task DeleteMyAccountAsync(CancellationToken cancellationToken) =>
        deleteAccountHandler.HandleAsync(cancellationToken);
}
