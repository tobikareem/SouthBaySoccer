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

/// <summary>
/// Command carrying the admin-entered, venue-local session details for create/publish. The backend
/// validates this with FluentValidation and stores UTC timestamps; the seed validates the same rules.
/// </summary>
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
    TimeSpan? RsvpDeadlineLocal = null);

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

