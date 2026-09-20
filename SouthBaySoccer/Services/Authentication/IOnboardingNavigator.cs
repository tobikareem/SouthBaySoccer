using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Services.Authentication;

/// <summary>Pushes the pre-authentication onboarding pages inside the Welcome Back navigation stack.</summary>
public interface IOnboardingNavigator
{
    Task ShowSignUpStartAsync(CancellationToken cancellationToken);
    Task ShowLinkWaitingAsync(OnboardingLinkKind kind, CancellationToken cancellationToken);
    Task ShowSignUpExpiredAsync(OnboardingTokenFailure failure, CancellationToken cancellationToken);
    Task ShowSignUpDetailsAsync(string token, string phoneMasked, CancellationToken cancellationToken);
    Task ShowSignUpWelcomeAsync(RegistrationCompletedResponse registration, CancellationToken cancellationToken);
    Task ShowSignInVerifyAsync(PhoneSignInStartResponse pendingSignIn, CancellationToken cancellationToken);
    Task PopAsync(CancellationToken cancellationToken);
    Task PopToWelcomeAsync(CancellationToken cancellationToken);
}
