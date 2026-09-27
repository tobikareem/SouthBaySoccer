using Microsoft.Maui.Storage;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Services.Sessions;

/// <summary>
/// <see cref="IDismissedStatsPromptStore"/> backed by <see cref="Preferences"/>. Dismissed session
/// ids are persisted as a semicolon-separated list under a single key, so the choice survives app
/// restarts without creating a preference key per session.
/// </summary>
public sealed class DismissedStatsPromptStore : IDismissedStatsPromptStore
{
    private const string Key = "stats_prompt.dismissed";
    private const char Separator = ';';

    public bool IsDismissed(Guid sessionId) =>
        Read().Contains(Token(sessionId), StringComparer.OrdinalIgnoreCase);

    public void Dismiss(Guid sessionId)
    {
        var token = Token(sessionId);
        var current = Read();
        if (current.Contains(token, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Preferences.Default.Set(Key, string.Join(Separator, current.Append(token)));
    }

    // "N" gives 32 hex digits with no separators, so it is safe to join with ';'.
    private static string Token(Guid sessionId) => sessionId.ToString("N");

    private static IReadOnlyList<string> Read() =>
        Preferences.Default.Get(Key, string.Empty)
            .Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
