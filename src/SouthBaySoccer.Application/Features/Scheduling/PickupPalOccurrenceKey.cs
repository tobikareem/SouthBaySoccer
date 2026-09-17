namespace SouthBaySoccer.Application.Features.Scheduling;

/// <summary>
/// The occurrence-key convention that marks a session as imported from Pickup Pal:
/// <c>pickuppal:{gameId}</c>. Both the import (which writes it) and the RSVP roster sync (which
/// reads it to decide whether a session is in scope) go through here.
/// </summary>
public static class PickupPalOccurrenceKey
{
    /// <summary>The prefix every imported session's occurrence key starts with.</summary>
    public const string Prefix = "pickuppal:";

    /// <summary>Builds the occurrence key for a Pickup Pal game id.</summary>
    public static string Build(string gameId) => $"{Prefix}{gameId}";

    /// <summary>
    /// Extracts the Pickup Pal game id from a session occurrence key. Returns false for app-only
    /// sessions (null, other prefixes, or an empty id).
    /// </summary>
    public static bool TryGetGameId(string? occurrenceKey, out string gameId)
    {
        gameId = string.Empty;
        if (occurrenceKey is null
            || !occurrenceKey.StartsWith(Prefix, StringComparison.Ordinal)
            || occurrenceKey.Length == Prefix.Length)
        {
            return false;
        }

        gameId = occurrenceKey[Prefix.Length..];
        return true;
    }
}
