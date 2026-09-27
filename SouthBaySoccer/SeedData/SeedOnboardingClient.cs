using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.SeedData;

/// <summary>
/// Deterministic onboarding for demos. Any token works except "expired" and "used"; the email
/// "taken@example.com" is already registered. Paste a link such as
/// southbaysoccer://auth/register?token=demo on the waiting screen to move on without WhatsApp.
/// </summary>
public sealed class SeedOnboardingClient : IOnboardingClient
{
    public const string TakenEmail = "taken@example.com";
    public const string TermsVersion = "20250708";

    private static readonly DateTime AccessTokenExpiresAtUtc = new(2099, 1, 1, 1, 0, 0, DateTimeKind.Utc);

    public Task<PhoneSignInStartResponse> BeginPhoneSignInAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PhoneSignInStartResponse(true, Mask(phoneNumber), "Ada Okafor", null));
    }

    public Task<AuthenticationTokensResponse> CompleteLoginAsync(string token, bool rememberDevice, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowForKnownFailures(token);
        return Task.FromResult(CreateTokens());
    }

    public Task<RegistrationTokenValidationResponse> ValidateRegistrationAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowForKnownFailures(token);
        return Task.FromResult(new RegistrationTokenValidationResponse("+1 (555) ••• 9421"));
    }

    public Task<bool> IsEmailAvailableAsync(string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!string.Equals(email.Trim(), TakenEmail, StringComparison.OrdinalIgnoreCase));
    }

    public Task<RegistrationCompletedResponse> RegisterAsync(RegisterWithWhatsAppRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowForKnownFailures(request.Token);
        return Task.FromResult(new RegistrationCompletedResponse(
            CreateTokens(),
            request.FirstName,
            "+1 (555) ••• 9421",
            ["Saturday crew"],
            HistorySyncPending: true));
    }

    public Task<string> GetTermsVersionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TermsVersion);
    }

    private static void ThrowForKnownFailures(string token)
    {
        switch (token.Trim().ToLowerInvariant())
        {
            case "expired":
                throw new OnboardingTokenException(OnboardingTokenFailure.Expired);
            case "used":
                throw new OnboardingTokenException(OnboardingTokenFailure.Invalid);
            case "registered":
                throw new OnboardingTokenException(OnboardingTokenFailure.AlreadyRegistered);
        }
    }

    private static string Mask(string phoneNumber)
    {
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? $"+{digits[..Math.Min(1, digits.Length)]} (•••) ••• {digits[^4..]}" : "•••";
    }

    private static AuthenticationTokensResponse CreateTokens() =>
        new("seed-access-token", "seed-refresh-token", AccessTokenExpiresAtUtc);
}
