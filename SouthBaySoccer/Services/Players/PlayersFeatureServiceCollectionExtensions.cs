using Microsoft.Extensions.DependencyInjection;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Pages;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Services.Players;

public sealed class ShellPlayersNavigator : IPlayersNavigator
{
    public Task OpenPlayerProfileAsync(Guid playerId)
    {
        if (playerId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        return Shell.Current.GoToAsync($"//profile?playerId={playerId}");
    }
}

public static class PlayersFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddPlayersFeature(this IServiceCollection services)
    {
        services.AddSingleton<IPlayersNavigator, ShellPlayersNavigator>();
        services.AddTransient<PlayersPageModel>();
        services.AddTransient<PlayersPage>();
        return services;
    }
}
