namespace SouthBaySoccer.Services.Clients;

/// <summary>
/// Navigation boundary for the Sessions home screen. Keeps the page model free of
/// MAUI Shell types so it stays unit-testable, mirroring how authentication navigation
/// is delegated through an injected abstraction rather than calling Shell directly.
/// </summary>
public interface ISessionsNavigator
{
    /// <summary>Opens the Session detail screen for the given session.</summary>
    Task GoToSessionAsync(Guid sessionId);

    /// <summary>Opens the Match stats screen for the given match.</summary>
    Task GoToMatchStatsAsync(Guid matchId);

    /// <summary>Opens the upcoming-session schedule screen.</summary>
    Task GoToScheduleAsync();
}
