namespace SouthBaySoccer.Services.Clients;

/// <summary>
/// Remembers which sessions the player has dismissed the "Submit your stats" prompt for. The server
/// keeps re-offering a claim prompt whenever a game still has an unclaimed entry; when the player
/// says "None of these are me" there is nothing for them to submit, so this hides that prompt
/// locally instead of letting it nag on every home-screen visit. Client-only state.
/// </summary>
public interface IDismissedStatsPromptStore
{
    /// <summary>True when the player has dismissed the stats prompt for <paramref name="sessionId"/>.</summary>
    bool IsDismissed(Guid sessionId);

    /// <summary>Records that the player dismissed the stats prompt for <paramref name="sessionId"/>.</summary>
    void Dismiss(Guid sessionId);
}
