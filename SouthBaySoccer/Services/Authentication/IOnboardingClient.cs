using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Services.Authentication;

/// <summary>
/// Sign-up and verified sign-in calls. Every method goes through SouthBaySoccer Functions; the
/// client never talks to Pickup Pal directly.
/// </summary>
public interface IOnboardingClient
{
    Task<PhoneSignInStartResponse> BeginPhoneSignInAsync(string phoneNumber, CancellationToken cancellationToken);

    /// <exception cref="OnboardingTokenException">The login token is expired, invalid, or for another user.</exception>
    Task<AuthenticationTokensResponse> CompleteLoginAsync(string token, bool rememberDevice, CancellationToken cancellationToken);

    /// <exception cref="OnboardingTokenException">The registration token is expired, invalid, or already registered.</exception>
    Task<RegistrationTokenValidationResponse> ValidateRegistrationAsync(string token, CancellationToken cancellationToken);

    Task<bool> IsEmailAvailableAsync(string email, CancellationToken cancellationToken);

    /// <exception cref="OnboardingTokenException">The registration token was spent or expired before submit.</exception>
    Task<RegistrationCompletedResponse> RegisterAsync(RegisterWithWhatsAppRequest request, CancellationToken cancellationToken);

    Task<string> GetTermsVersionAsync(CancellationToken cancellationToken);
}
