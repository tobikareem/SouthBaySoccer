namespace SouthBaySoccer.Contracts.Sessions;

/// <summary>
/// Defaults used to seed the admin "Create session" form (ADMIN-4). The backend will derive these
/// from the organiser's group, saved venues, and the requester's <c>CanManageSessions</c> permission;
/// the UI-first seed returns a deterministic set. All date/time values are venue-local because the
/// admin form edits in local time and the backend converts to UTC on create.
/// </summary>
public sealed record CreateSessionDefaultsDto(
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
    VenueDto SavedVenue,
    string FeedLabel,
    TimeSpan? DefaultRsvpDeadlineLocal = null);

/// <summary>A selectable playing venue surfaced by venue search.</summary>
public sealed record VenueDto(
    Guid Id,
    string Name,
    string Locality,
    bool IsSaved);

/// <summary>Compact row for sessions that an admin can open and update.</summary>
public sealed record ManagedSessionDto(
    Guid SessionId,
    string Title,
    string DateLabel,
    string TimeLabel,
    string VenueName,
    string Format,
    int Capacity,
    string StatusLabel);

/// <summary>Editable session details loaded into the admin create/update form.</summary>
public sealed record ManagedSessionEditDto(
    Guid SessionId,
    CreateSessionCommand Command,
    bool IsPublished);

/// <summary>
/// Command carrying the admin-entered, venue-local session details for create/publish. The backend
/// validates this with FluentValidation and stores UTC timestamps; the seed validates the same rules.
/// </summary>
/// <remarks>
/// <see cref="CheckInOpenLocal"/>, <see cref="CheckInCloseLocal"/>, and <see cref="RsvpDeadlineLocal"/>
/// are times-of-day; each is paired with a day-offset field expressing how many days its local date
/// falls before (negative) or after (positive) <see cref="GameDateLocal"/>. Carrying the offset
/// explicitly — rather than always re-deriving the date from <see cref="GameDateLocal"/> — lets an
/// edit round-trip preserve a deadline set the evening before the game instead of silently coercing it
/// onto game day. All three default to 0 (same day as the game), which covers the common case and
/// keeps existing callers source-compatible.
/// </remarks>
public sealed record CreateSessionCommand(
    DateTime GameDateLocal,
    TimeSpan StartTimeLocal,
    TimeSpan CheckInOpenLocal,
    TimeSpan CheckInCloseLocal,
    Guid? VenueId,
    string VenueName,
    string Format,
    int Capacity,
    int TeamCount,
    TimeSpan? RsvpDeadlineLocal = null,
    int CheckInOpenDayOffset = 0,
    int CheckInCloseDayOffset = 0,
    int RsvpDeadlineDayOffset = 0);

/// <summary>
/// Result of a create-draft or publish operation. Mirrors <c>ClientCommandResult</c> but also carries
/// the resulting session id so the page model can navigate to the published session.
/// </summary>
public sealed record CreateSessionResult(
    bool IsSuccess,
    Guid SessionId,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static CreateSessionResult Success(Guid sessionId) => new(true, sessionId);

    public static CreateSessionResult Failure(string errorCode, string errorMessage) =>
        new(false, Guid.Empty, errorCode, errorMessage);
}

