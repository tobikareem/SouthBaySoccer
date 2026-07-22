namespace SouthBaySoccer.Application.Features.Scheduling;

/// <summary>
/// Reads active games from the Pickup Pal bot API. Implementations must return only sanitized
/// game data: WhatsApp JIDs, group ids, and subscriber ids never cross this boundary.
/// </summary>
public interface IPickupPalGamesClient
{
    /// <summary>Gets the currently active Pickup Pal games with their participants.</summary>
    Task<IReadOnlyList<PickupPalGame>> GetActiveGamesAsync(CancellationToken cancellationToken = default);
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

/// <summary>One sanitized participant on a Pickup Pal game.</summary>
public sealed record PickupPalGameParticipantInfo(
    string Id,
    string DisplayName,
    bool IsGuest,
    bool IsWaitlist,
    DateTime JoinedAtUtc);
