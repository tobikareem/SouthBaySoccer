using SouthBaySoccer.Application.Common;

namespace SouthBaySoccer.Application.Features.Onboarding;

/// <summary>Why Pickup Pal rejected an onboarding call. Values are safe to surface and log.</summary>
public enum PickupPalOnboardingFailure
{
    /// <summary>The token is unknown or already redeemed.</summary>
    TokenInvalid,

    /// <summary>The token has expired.</summary>
    TokenExpired,

    /// <summary>Pickup Pal already has an account with this email.</summary>
    EmailAlreadyRegistered,

    /// <summary>Pickup Pal already has an account with the phone bound to the token.</summary>
    PhoneAlreadyRegistered,

    /// <summary>Pickup Pal rejected the payload for another validation reason.</summary>
    Validation,
}

/// <summary>Thrown by <see cref="IPickupPalOnboardingClient"/> when Pickup Pal rejects a call.</summary>
public sealed class PickupPalOnboardingException : ApplicationExceptionBase
{
    /// <summary>Initializes a new instance of the <see cref="PickupPalOnboardingException"/> class.</summary>
    /// <param name="failure">The classified failure.</param>
    /// <param name="detail">A safe, non-personal detail from Pickup Pal's error string, when present.</param>
    public PickupPalOnboardingException(PickupPalOnboardingFailure failure, string? detail = null)
        : base(detail ?? $"Pickup Pal rejected the request: {failure}.")
    {
        Failure = failure;
        Detail = detail;
    }

    /// <summary>Gets the classified failure.</summary>
    public PickupPalOnboardingFailure Failure { get; }

    /// <summary>Gets the safe detail, when Pickup Pal supplied one.</summary>
    public string? Detail { get; }
}

/// <summary>Token outcomes the client distinguishes with stable problem types.</summary>
public enum OnboardingTokenFailure
{
    /// <summary>The token has expired (HTTP 410).</summary>
    Expired,

    /// <summary>The token is unknown or already used (HTTP 404).</summary>
    Invalid,

    /// <summary>The proven phone already has a Pickup Pal account (HTTP 409).</summary>
    AlreadyRegistered,

    /// <summary>The email already has a Pickup Pal account (HTTP 409).</summary>
    EmailAlreadyRegistered,

    /// <summary>The login token resolved to a user other than the pending sign-in (HTTP 403).</summary>
    Mismatch,
}

/// <summary>Thrown when an onboarding token cannot complete the flow it was presented for.</summary>
public sealed class OnboardingTokenException : ApplicationExceptionBase
{
    /// <summary>Initializes a new instance of the <see cref="OnboardingTokenException"/> class.</summary>
    /// <param name="failure">The token outcome.</param>
    public OnboardingTokenException(OnboardingTokenFailure failure)
        : base($"Onboarding token failure: {failure}.")
    {
        Failure = failure;
    }

    /// <summary>Gets the token outcome.</summary>
    public OnboardingTokenFailure Failure { get; }
}
