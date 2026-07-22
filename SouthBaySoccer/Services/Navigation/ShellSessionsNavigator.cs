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

    public Task GoToMatchStatsAsync(Guid matchId)
    {
        if (matchId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        return Shell.Current.GoToAsync($"matchstats?matchId={matchId}");
    }

    public Task GoToScheduleAsync() =>
        Shell.Current.GoToAsync("schedule");

    public Task GoToCreateSessionAsync() =>
        Shell.Current.GoToAsync("create-session");
}
