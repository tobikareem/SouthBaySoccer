namespace SouthBaySoccer.Application.Features.Onboarding;

/// <summary>
/// Configured onboarding rules. Infrastructure binds these from the <c>Onboarding</c> settings section
/// so the Application layer never depends on the configuration system.
/// </summary>
public interface IOnboardingPolicy
{
    /// <summary>
    /// Gets a value indicating whether phone sign-in must be completed with a WhatsApp login token
    /// before tokens are issued. Remember-device is handled by refresh tokens, not by this switch.
    /// </summary>
    bool RequireWhatsAppVerification { get; }

    /// <summary>Gets how long a pending phone sign-in may wait for its login token.</summary>
    TimeSpan PendingSignInLifetime { get; }

    /// <summary>Gets the refresh-token lifetime used when the player asks to remember the device.</summary>
    TimeSpan RememberDeviceRefreshTokenLifetime { get; }

    /// <summary>
    /// Gets the refresh-token lifetime for a verified sign-in that did not ask to remember the
    /// device: long enough to survive an evening of use, short enough that the next day's launch
    /// goes through WhatsApp verification again.
    /// </summary>
    TimeSpan SessionRefreshTokenLifetime { get; }

    /// <summary>Gets the current terms version every registration must accept.</summary>
    string TermsVersion { get; }

    /// <summary>
    /// Determines whether the phone number is exempt from WhatsApp verification (for example the
    /// App Review demo accounts). Compared transiently on the normalized E.164 value; never stored.
    /// </summary>
    /// <param name="phoneNumber">The phone number in E.164 form (<c>+digits</c>).</param>
    bool IsVerificationExempt(string phoneNumber);
}
