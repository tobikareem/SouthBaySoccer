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
}

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
