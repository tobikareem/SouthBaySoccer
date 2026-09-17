namespace SouthBaySoccer.Contracts.Rsvps;

public sealed record SubmitRsvpRequest(string Status);

/// <param name="PickupPalSync">
/// Whether the RSVP was mirrored onto the Pickup Pal roster of an imported game:
/// <c>NotApplicable</c> (app-only session or no Pickup Pal id), <c>Synced</c>, <c>Pending</c>
/// (retry scheduled), or <c>Failed</c>. Additive; older clients ignore it.
/// </param>
public sealed record RsvpResponseDto(
    Guid SessionId,
    Guid PlayerProfileId,
    string State,
    Guid? RsvpResponseId,
    Guid? WaitlistEntryId,
    int? WaitlistPosition,
    Guid? PromotedPlayerProfileId,
    string PickupPalSync = "NotApplicable");

public sealed record AdminOverrideRsvpRequest(
    Guid PlayerProfileId,
    string Reason);

public sealed record CheckInPlayerRequest(
    Guid PlayerProfileId,
    string Outcome,
    string? LateOverrideReason = null);

public sealed record CheckInResponseDto(
    Guid CheckInId,
    Guid SessionId,
    Guid PlayerProfileId,
    Guid CheckedInByPlayerProfileId,
    DateTime CheckedInAtUtc,
    string Outcome,
    bool IsLateOverride,
    Guid? AdminOverrideId,
    string? LateOverrideReason);

public sealed record NoShowResponseDto(Guid SessionId, int RecordedCount);
