using Microsoft.Extensions.DependencyInjection;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Pages;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Services.Leaderboard;

public sealed class ShellLeaderboardNavigator : ILeaderboardNavigator
{
    public Task OpenPlayerProfileAsync(Guid playerId)
    {
        if (playerId == Guid.Empty)
        {
            return Shell.Current.GoToAsync("//profile");
        }

        // Push a profile *detail* page rather than switching to the Profile tab. Routing another
        // player through "//profile" reuses the tab's cached page model, so its requestedPlayerId
        // survives and tapping the Profile tab afterwards keeps showing that player. Mirrors
        // ShellPlayersNavigator.
        return Shell.Current.GoToAsync($"player-profile?playerId={playerId}");
    }
}

public static class LeaderboardFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddLeaderboardFeature(this IServiceCollection services)
    {
        services.AddSingleton(new LeaderboardOptions());
        services.AddSingleton<ILeaderboardNavigator, ShellLeaderboardNavigator>();
        services.AddTransient<LeaderboardPageModel>();
        services.AddTransient<StatsPage>();
        return services;
    }
}
