using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Application.Features.Rsvps;

public sealed record RsvpResultModel(
    Guid SessionId,
    Guid PlayerProfileId,
    string State,
    Guid? RsvpResponseId,
    Guid? WaitlistEntryId,
    int? WaitlistPosition,
    Guid? PromotedPlayerProfileId);

public sealed record SubmitRsvpCommand(
    Guid SessionId,
    RsvpStatus Status);

public sealed record CancelRsvpCommand(Guid SessionId);

public sealed record AdminOverrideRsvpCommand(
    Guid SessionId,
    Guid PlayerProfileId,
    string Reason);

public sealed record CheckInPlayerCommand(
    Guid SessionId,
    Guid PlayerProfileId,
    AttendanceOutcome Outcome,
    string? LateOverrideReason = null);

public sealed record SelfCheckInCommand(Guid SessionId);

public sealed record RecordNoShowsCommand(Guid SessionId);

public sealed record CheckInResultModel(
    Guid CheckInId,
    Guid SessionId,
    Guid PlayerProfileId,
    Guid CheckedInByPlayerProfileId,
    DateTime CheckedInAtUtc,
    string Outcome,
    bool IsLateOverride,
    Guid? AdminOverrideId,
    string? LateOverrideReason);

public sealed record NoShowResultModel(Guid SessionId, int RecordedCount);
