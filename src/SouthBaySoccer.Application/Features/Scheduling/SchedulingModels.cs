using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed record SeasonModel(
    Guid SeasonId,
    string Name,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc);

public sealed record CreateSeasonCommand(
    string Name,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc);

public sealed record VenueModel(
    Guid VenueId,
    string Name,
    string Locality,
    string? Address,
    string? MapsProviderReference);

public sealed record CreateVenueCommand(
    string Name,
    string Locality,
    string? Address);

public sealed record SessionModel(
    Guid SessionId,
    Guid SeasonId,
    Guid VenueId,
    Guid? RecurrenceRuleId,
    string Title,
    string Format,
    int Capacity,
    int TeamCount,
    DateTime StartsAtUtc,
    DateTime CheckInOpensAtUtc,
    DateTime CheckInClosesAtUtc,
    DateTime RsvpDeadlineUtc,
    string? OccurrenceKey,
    string Status);

public sealed record SessionFeedModel(
    SessionModel Session,
    string VenueName,
    int GoingCount,
    int WaitlistCount,
    bool IsFull,
    bool IsCurrentPlayerGoing,
    bool IsCurrentPlayerWaitlisted,
    bool CanJoinWaitlist,
    string? GroupName = null);

public sealed record CreateSessionCommand(
    Guid SeasonId,
    Guid VenueId,
    string Title,
    string Format,
    int Capacity,
    int TeamCount,
    DateTime StartsAtUtc,
    DateTime CheckInOpensAtUtc,
    DateTime CheckInClosesAtUtc,
    DateTime RsvpDeadlineUtc,
    Guid? RecurrenceRuleId = null,
    string? OccurrenceKey = null,
    SessionStatus Status = SessionStatus.Published);

public sealed record CreateSessionAdminDefaultsModel(
    bool CanManageSessions,
    DateTime DefaultGameDateLocal,
    TimeSpan DefaultStartTimeLocal,
    int CheckInLeadMinutes,
    int CheckInCloseOffsetMinutes,
    IReadOnlyList<string> Formats,
    int DefaultFormatIndex,
    int DefaultCapacity,
    int MinimumCapacity,
    int MaximumCapacity,
    IReadOnlyList<string> TeamOptions,
    int DefaultTeamIndex,
    VenueModel SavedVenue,
    string FeedLabel,
    TimeSpan? DefaultRsvpDeadlineLocal);

public sealed record ManagedSessionModel(
    Guid SessionId,
    string Title,
    DateTime StartsAtUtc,
    string VenueName,
    string Format,
    int Capacity,
    string Status);

public sealed record ManagedSessionEditModel(
    Guid SessionId,
    Guid VenueId,
    string VenueName,
    string Format,
    int Capacity,
    int TeamCount,
    DateTime StartsAtUtc,
    DateTime CheckInOpensAtUtc,
    DateTime CheckInClosesAtUtc,
    DateTime RsvpDeadlineUtc,
    string Status);

public sealed record CreateSessionDraftCommand(
    Guid? VenueId,
    string VenueName,
    string Format,
    int Capacity,
    int TeamCount,
    DateTime StartsAtUtc,
    DateTime CheckInOpensAtUtc,
    DateTime CheckInClosesAtUtc,
    DateTime RsvpDeadlineUtc);

public sealed record UpdateSessionAdminCommand(
    Guid SessionId,
    Guid? VenueId,
    string VenueName,
    string Format,
    int Capacity,
    int TeamCount,
    DateTime StartsAtUtc,
    DateTime CheckInOpensAtUtc,
    DateTime CheckInClosesAtUtc,
    DateTime RsvpDeadlineUtc);

public sealed record CancelSessionCommand(
    Guid SessionId,
    string Reason);

public sealed record DeleteSessionCommand(Guid SessionId);

public sealed record CreateRecurrenceRuleCommand(
    string Name,
    string TimeZoneId,
    string Rule);

public sealed record RecurrenceRuleModel(
    Guid RecurrenceRuleId,
    string Name,
    string TimeZoneId,
    string Rule);

public sealed record CreateSessionOccurrenceCommand(
    Guid RecurrenceRuleId,
    Guid SeasonId,
    Guid VenueId,
    DateTime OccurrenceStartsAtUtc,
    string Title,
    string Format,
    int Capacity,
    int TeamCount,
    DateTime CheckInOpensAtUtc,
    DateTime CheckInClosesAtUtc,
    DateTime RsvpDeadlineUtc);
