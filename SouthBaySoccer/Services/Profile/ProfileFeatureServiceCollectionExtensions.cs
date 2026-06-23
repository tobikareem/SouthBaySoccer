using Microsoft.Extensions.DependencyInjection;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Pages;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Services.Profile;

public sealed class ProfileOptions
{
    public Uri AccountUri { get; init; } = new("https://pickuppal.app/account");
}

public sealed class ProfileExternalLauncher(ProfileOptions options) : IProfileExternalLauncher
{
    public async Task<bool> OpenAccountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!await Launcher.Default.CanOpenAsync(options.AccountUri))
        {
            return false;
        }

        return await Launcher.Default.OpenAsync(options.AccountUri);
    }
}

public sealed class ShellProfileNavigator : IProfileNavigator
{
    public Task OpenLeaderboardAsync() => Shell.Current.GoToAsync("//stats");
}

public static class ProfileFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddProfileFeature(this IServiceCollection services)
    {
        services.AddSingleton(new ProfileOptions());
        services.AddSingleton<IProfileExternalLauncher, ProfileExternalLauncher>();
        services.AddSingleton<IProfileNavigator, ShellProfileNavigator>();
        services.AddTransient<ProfilePageModel>();
        services.AddTransient<ProfilePage>();
        return services;
    }
}
