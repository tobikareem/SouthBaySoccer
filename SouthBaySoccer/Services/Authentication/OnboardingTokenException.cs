namespace SouthBaySoccer.Services.Authentication;

/// <summary>Why a bot-issued link token could not be used.</summary>
public enum OnboardingTokenFailure
{
    Expired,
    Invalid,
    AlreadyRegistered,
    Mismatch
}

public sealed class OnboardingTokenException(OnboardingTokenFailure failure)
    : Exception($"Onboarding token failed: {failure}.")
{
    public OnboardingTokenFailure Failure { get; } = failure;
}
