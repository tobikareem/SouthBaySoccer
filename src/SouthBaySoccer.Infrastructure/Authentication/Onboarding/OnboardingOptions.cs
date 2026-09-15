namespace SouthBaySoccer.Infrastructure.Authentication.Onboarding;

/// <summary>
/// Options for WhatsApp-verified onboarding. Bound from the <c>Onboarding</c> configuration section.
/// </summary>
public sealed class OnboardingOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether phone sign-in requires the <c>!!login</c> WhatsApp
    /// round-trip before tokens are issued. Defaults to <see langword="true"/>.
    /// </summary>
    public bool RequireWhatsAppVerification { get; set; } = true;

    /// <summary>
    /// Gets or sets a comma-separated list of phone numbers exempt from WhatsApp verification, for
    /// the App Review demo accounts. Normalized like <c>AdminPhoneNumbers</c> (to <c>+digits</c>).
    /// </summary>
    public string VerificationExemptPhoneNumbers { get; set; } = string.Empty;

    /// <summary>Gets or sets the current terms version served to and required from the client.</summary>
    public string TermsVersion { get; set; } = "20250708";

    /// <summary>Gets or sets how long a pending phone sign-in may wait for its login token.</summary>
    public TimeSpan PendingSignInLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets or sets the refresh-token lifetime issued when the player remembers the device.</summary>
    public TimeSpan RememberDeviceRefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
}
