namespace SouthBaySoccer.Application.Common;

/// <summary>
/// Builds display initials (e.g. "AJ") from a player's display name. This is the single Application-
/// layer source for that computation so every feature that renders a player's initials (directory,
/// profile, leaderboards) agrees for the same player. The blank-name fallback matches the MAUI client's
/// own local fallback (<c>ApiProfileClient.BuildInitials</c> uses "SB") so a player renders identically
/// whether the initials came from the server or were computed client-side against cached data.
/// </summary>
public static class PlayerInitials
{
    private const string FallbackInitials = "SB";

    public static string Build(string displayName)
    {
        var initials = string.Concat(
            displayName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0])));

        return string.IsNullOrWhiteSpace(initials) ? FallbackInitials : initials;
    }
}
