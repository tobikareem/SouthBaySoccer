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
    string? OccurrenceKey = null);

public sealed record CancelSessionCommand(
    Guid SessionId,
    string Reason);

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
