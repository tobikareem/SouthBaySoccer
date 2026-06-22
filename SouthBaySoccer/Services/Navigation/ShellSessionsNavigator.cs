using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Services.Navigation;

/// <summary>
/// Shell-backed implementation of <see cref="ISessionsNavigator"/>. Lives in the MAUI app so the
/// page model and unit tests stay free of Shell. The Session detail route/param contract is fixed
/// as <c>session?sessionId={id}</c>.
/// </summary>
public sealed class ShellSessionsNavigator : ISessionsNavigator
{
    public Task GoToSessionAsync(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        return Shell.Current.GoToAsync($"session?sessionId={sessionId}");
    }

    // Interim: the dedicated match-stats screen is a future ticket. Until it exists, surface the
    // Stats tab so the dashboard's stats prompt has a safe, sensible destination (matchId carried
    // forward once that screen lands).
    public Task GoToMatchStatsAsync(Guid _) =>
        Shell.Current.GoToAsync("//stats");

    // Interim: the full schedule screen is a future ticket; the Sessions tab is the schedule home.
    public Task GoToScheduleAsync() =>
        Shell.Current.GoToAsync("//sessions");
}
