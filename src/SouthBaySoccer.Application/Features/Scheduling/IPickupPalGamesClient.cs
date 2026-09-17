namespace SouthBaySoccer.Application.Features.Scheduling;

/// <summary>
/// Reads active games from the Pickup Pal bot API. Implementations must return only sanitized
/// game data: WhatsApp JIDs, group ids, and subscriber ids never cross this boundary.
/// </summary>
public interface IPickupPalGamesClient
{
    /// <summary>Gets the currently active Pickup Pal games with their participants.</summary>
    Task<IReadOnlyList<PickupPalGame>> GetActiveGamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one game (same shape as an active-feed item), or null when Pickup Pal no longer has it.</summary>
    /// <exception cref="Common.ApplicationServiceUnavailableException">Pickup Pal could not be reached or refused the call.</exception>
    Task<PickupPalGame?> GetGameAsync(string gameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a registered Pickup Pal user to a game's roster (Pickup Pal decides whether they land
    /// on the going list or its waitlist). Terminal outcomes are returned; transient failures throw.
    /// </summary>
    /// <exception cref="Common.ApplicationServiceUnavailableException">Pickup Pal could not be reached or refused the call.</exception>
    Task<PickupPalRosterPushResult> AddPlayerAsync(
        string gameId,
        string playerId,
        string playerName,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a registered Pickup Pal user from a game's roster. A player who is not on it counts as removed.</summary>
    /// <exception cref="Common.ApplicationServiceUnavailableException">Pickup Pal could not be reached or refused the call.</exception>
    Task<PickupPalRosterPushResult> RemovePlayerAsync(
        string gameId,
        string playerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a WhatsApp-group game on Pickup Pal for an app-published session. The game id comes
    /// back in <see cref="PickupPalGameCreateResult.GameId"/> when the result is
    /// <see cref="PickupPalGameWriteResult.Applied"/>. Terminal rejections are returned; transient
    /// failures throw.
    /// </summary>
    /// <exception cref="Common.ApplicationServiceUnavailableException">Pickup Pal could not be reached or refused the call.</exception>
    Task<PickupPalGameCreateResult> CreateGameAsync(
        PickupPalGameCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the location, capacity, and (when supplied) start of a game the app created.
    /// Pickup Pal's contract documents only location and maxPlayers; the start fields are sent as
    /// well and may be ignored upstream.
    /// </summary>
    /// <exception cref="Common.ApplicationServiceUnavailableException">Pickup Pal could not be reached or refused the call.</exception>
    Task<PickupPalGameWriteResult> UpdateGameAsync(
        string gameId,
        PickupPalGameUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Terminates a game the app created. A game Pickup Pal no longer has reports <see cref="PickupPalGameWriteResult.GameNotFound"/>.</summary>
    /// <exception cref="Common.ApplicationServiceUnavailableException">Pickup Pal could not be reached or refused the call.</exception>
    Task<PickupPalGameWriteResult> TerminateGameAsync(
        string gameId,
        CancellationToken cancellationToken = default);
}

/// <summary>Terminal outcome of a game create, update, or terminate on Pickup Pal.</summary>
public enum PickupPalGameWriteResult
{
    /// <summary>Pickup Pal applied the change.</summary>
    Applied,

    /// <summary>Pickup Pal no longer has the game.</summary>
    GameNotFound,

    /// <summary>Pickup Pal rejected the request (for example a 400 with a message); not retryable.</summary>
    Rejected,

    /// <summary>
    /// Pickup Pal answered success but the response carried no readable game id. Terminal on
    /// purpose: retrying a create would risk a duplicate game on Pickup Pal.
    /// </summary>
    InvalidResponse,
}

/// <summary>Outcome of a game create; <see cref="GameId"/> is set only when <see cref="Result"/> is <see cref="PickupPalGameWriteResult.Applied"/>.</summary>
public sealed record PickupPalGameCreateResult(PickupPalGameWriteResult Result, string? GameId);

/// <summary>
/// Fields Pickup Pal needs to create a WhatsApp-group game. The client fixes <c>gameType</c> to
/// <c>WHATSAPP_GROUP</c> and <c>sport</c> to <c>SOCCER</c>, formats the start as the group-local
/// <c>date</c>/<c>time</c> in <paramref name="TimeZoneId"/>, and omits latitude/longitude.
/// </summary>
/// <param name="GroupId">The WhatsApp group id (<c>GroupChat.ExternalId</c>). Personal data: never logged.</param>
/// <param name="StartsAtUtc">The session start (UTC).</param>
/// <param name="TimeZoneId">The IANA zone the date and time are expressed in.</param>
/// <param name="Location">Venue name plus address when present.</param>
/// <param name="MaxPlayers">The session capacity.</param>
/// <param name="CreatorId">The acting admin's Pickup Pal user id. Personal data: never logged.</param>
public sealed record PickupPalGameCreateRequest(
    string GroupId,
    DateTime StartsAtUtc,
    string TimeZoneId,
    string Location,
    int MaxPlayers,
    string CreatorId);

/// <summary>
/// Fields for a game update. <paramref name="StartsAtUtc"/> is sent as group-local
/// <c>date</c>/<c>time</c> plus <c>timezone</c> only when it is not null (the start changed).
/// </summary>
public sealed record PickupPalGameUpdateRequest(
    string Location,
    int MaxPlayers,
    DateTime? StartsAtUtc,
    string TimeZoneId);

/// <summary>Terminal outcome of a roster add or remove on Pickup Pal.</summary>
public enum PickupPalRosterPushResult
{
    /// <summary>Pickup Pal applied the change.</summary>
    Applied,

    /// <summary>Pickup Pal reported the roster already in the requested state.</summary>
    AlreadyApplied,

    /// <summary>Pickup Pal refused the add because the game is full.</summary>
    GameFull,

    /// <summary>Pickup Pal no longer has the game.</summary>
    GameNotFound,

    /// <summary>Pickup Pal rejected the request for another non-retryable reason.</summary>
    Rejected,
}

/// <summary>One sanitized Pickup Pal active game.</summary>
public sealed record PickupPalGame(
    string Id,
    DateTime StartsAtUtc,
    string Location,
    int MaxPlayers,
    string Status,
    string GroupName,
    IReadOnlyList<PickupPalGameParticipantInfo> Participants);

/// <summary>
/// One sanitized participant on a Pickup Pal game. Phone and WhatsApp identities arrive pre-hashed
/// (plus a masked phone for display) so raw values never cross this boundary; the identity fields
/// are excluded from serialization to keep persisted snapshots limited to display data.
/// </summary>
public sealed record PickupPalGameParticipantInfo(
    string Id,
    string DisplayName,
    bool IsGuest,
    bool IsWaitlist,
    DateTime JoinedAtUtc,
    [property: System.Text.Json.Serialization.JsonIgnore] string? UserId = null,
    [property: System.Text.Json.Serialization.JsonIgnore] string? PhoneNumberHash = null,
    [property: System.Text.Json.Serialization.JsonIgnore] string? MaskedPhoneNumber = null,
    [property: System.Text.Json.Serialization.JsonIgnore] string? WhatsAppJidHash = null);
