using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Services.Authentication;

public enum OnboardingLinkKind
{
    Register,
    Login
}

/// <summary>
/// Owns the in-memory state of a sign-up or verified sign-in in progress: which link we are
/// waiting for, when it was requested, and the matched account for sign-in. Tokens are never
/// persisted; the flow is reset whenever a link is consumed or the user backs out.
/// </summary>
public interface IOnboardingFlow
{
    /// <summary>The two WhatsApp messages the app prefills. Copy lives here so pages and tests agree.</summary>
    string RegisterMessage { get; }
    string LoginMessage { get; }

    OnboardingLinkKind? PendingKind { get; }
    DateTimeOffset? LinkRequestedAt { get; }
    PhoneSignInStartResponse? PendingSignIn { get; }
    bool RememberDevice { get; set; }

    /// <summary>True when the last WhatsApp handoff could not open WhatsApp; the waiting page shows help.</summary>
    bool LastHandoffFailed { get; }

    Task ShowSignUpAsync(CancellationToken cancellationToken);

    /// <summary>Opens WhatsApp with !!register prefilled and shows the waiting screen.</summary>
    Task StartSignUpHandoffAsync(CancellationToken cancellationToken);

    /// <summary>Records the matched account and shows the verify screen.</summary>
    Task BeginSignInVerificationAsync(PhoneSignInStartResponse pendingSignIn, CancellationToken cancellationToken);

    /// <summary>Opens WhatsApp with !!login prefilled and shows the waiting screen.</summary>
    Task StartSignInHandoffAsync(CancellationToken cancellationToken);

    /// <summary>Re-opens WhatsApp with the message for the pending link kind.</summary>
    Task<bool> ReopenWhatsAppAsync(CancellationToken cancellationToken);

    /// <summary>Routes a register/login app link. Returns false when the URI is not ours.</summary>
    Task<bool> HandleAppLinkAsync(Uri uri, CancellationToken cancellationToken);

    /// <summary>Same as <see cref="HandleAppLinkAsync"/> for text pasted from the clipboard.</summary>
    Task<bool> HandlePastedLinkAsync(string? text, CancellationToken cancellationToken);

    /// <summary>Redeems a registration token and moves to the details form or the expired screen.</summary>
    Task HandleRegistrationTokenAsync(string token, CancellationToken cancellationToken);

    /// <summary>Redeems a login token for the pending sign-in and enters the app.</summary>
    Task HandleLoginTokenAsync(string token, CancellationToken cancellationToken);

    /// <summary>Called by the expired screen: marks the pending link as expired for a fresh handoff.</summary>
    void Reset();
}
