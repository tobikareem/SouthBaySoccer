namespace SouthBaySoccer.Contracts.Sessions;

public sealed record SeasonResponse(
    Guid SeasonId,
    string Name,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc);

public sealed record CreateSeasonRequest(
    string Name,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc);

public sealed record VenueResponse(
    Guid VenueId,
    string Name,
    string Locality,
    string? Address,
    string? MapsProviderReference);

public sealed record CreateVenueRequest(
    string Name,
    string Locality,
    string? Address);

public sealed record SessionAdminResponse(
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
    string Status,
    string VenueName = "",
    int GoingCount = 0,
    int WaitlistCount = 0,
    bool IsFull = false,
    bool IsCurrentPlayerGoing = false,
    bool IsCurrentPlayerWaitlisted = false,
    bool CanJoinWaitlist = false,
    string? GroupName = null);

public sealed record CreateSessionAdminRequest(
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
    string? OccurrenceKey = null);

public sealed record CancelSessionRequest(string Reason);

public sealed record RecurrenceRuleResponse(
    Guid RecurrenceRuleId,
    string Name,
    string TimeZoneId,
    string Rule);

public sealed record CreateRecurrenceRuleRequest(
    string Name,
    string TimeZoneId,
    string Rule);

public sealed record CreateSessionOccurrenceRequest(
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
