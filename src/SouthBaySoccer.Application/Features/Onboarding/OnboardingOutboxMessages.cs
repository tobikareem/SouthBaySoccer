namespace SouthBaySoccer.Application.Features.Onboarding;

/// <summary>
/// Outbox message types written by the onboarding handlers. There is no outbox processor yet
/// (nothing in the solution drains <c>OutboxMessages</c>); these rows are the durable record an
/// operator or a future timer trigger uses to reconcile Pickup Pal with our database.
/// </summary>
public static class OnboardingOutboxMessages
{
    /// <summary>Pickup Pal could not be reached while creating the account; the local registration is <c>ExternalFailed</c>.</summary>
    public const string PlayerRegistrationExternalFailed = "PlayerRegistrationExternalFailed";

    /// <summary>A player deleted their account and the Pickup Pal user must be deleted too.</summary>
    public const string PickupPalUserDeletionRequested = "PickupPalUserDeletionRequested";
}
