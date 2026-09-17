using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Application.Features.Scheduling;

namespace SouthBaySoccer.Application.Features.Outbox;

/// <summary>
/// The outbox message types the processor drains. A compile-time list, so the timer can claim rows
/// without resolving every scoped handler first, and so adding a handler is a deliberate two-line
/// change (register it, list it) that a test can verify stays in sync.
/// </summary>
public static class OutboxMessageTypes
{
    /// <summary>Message types with a registered <see cref="IOutboxMessageHandler"/>.</summary>
    public static readonly IReadOnlyList<string> Handled =
    [
        RsvpOutboxMessages.RsvpPickupPalSyncRequested,
        SessionOutboxMessages.SessionPickupPalSyncRequested,
        OnboardingOutboxMessages.PickupPalUserDeletionRequested,
    ];
}
