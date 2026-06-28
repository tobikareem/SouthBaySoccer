namespace SouthBaySoccer.Contracts.Rsvps;

public sealed record SubmitRsvpRequest(string Status);

public sealed record RsvpResponseDto(
    Guid SessionId,
    Guid PlayerProfileId,
    string State,
    Guid? RsvpResponseId,
    Guid? WaitlistEntryId,
    int? WaitlistPosition,
    Guid? PromotedPlayerProfileId);

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
