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

        // Push a profile *detail* page rather than switching to the Profile tab. Routing another
        // player through "//profile" reused the tab's cached page model, so its requestedPlayerId
        // survived and tapping the Profile tab afterwards kept showing that player instead of you.
        return Shell.Current.GoToAsync($"player-profile?playerId={playerId}");
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
