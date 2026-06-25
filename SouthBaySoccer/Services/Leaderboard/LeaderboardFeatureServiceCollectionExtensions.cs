using Microsoft.Extensions.DependencyInjection;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Pages;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Services.Leaderboard;

public sealed class ShellLeaderboardNavigator : ILeaderboardNavigator
{
    public Task OpenPlayerProfileAsync(Guid playerId)
    {
        _ = playerId;
        return Shell.Current.GoToAsync("//profile");
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
