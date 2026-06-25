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

        return Shell.Current.GoToAsync($"//profile?playerId={playerId}");
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
