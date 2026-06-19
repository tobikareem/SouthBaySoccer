using Microsoft.Extensions.DependencyInjection;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Services.Authentication;

#if !RELEASE
using SouthBaySoccer.SeedData;
#endif

namespace SouthBaySoccer.Services.Clients;

public static class ClientServiceCollectionExtensions
{
    public static IServiceCollection AddSouthBaySoccerClients(
        this IServiceCollection services,
        ClientDataSourceOptions options,
        PickupPalOptions pickupPalOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pickupPalOptions);

        services.AddSingleton(options);
        ClientDataSourceValidator.Validate(options, IsSeedProviderAvailable);

        return options.DataSource switch
        {
            ClientDataSource.Seed => AddSeedClients(services),
            ClientDataSource.Api => services,
            _ => throw new InvalidOperationException(
                $"Unsupported client data source '{options.DataSource}'.")
        };
    }

    private static IServiceCollection AddSeedClients(IServiceCollection services)
    {
#if RELEASE
        throw new InvalidOperationException(
            "ClientDataSource 'Seed' is unavailable in Release builds.");
#else
        services.AddSingleton<SeedState>();
        services.AddSingleton<IAuthenticationClient, SeedAuthenticationClient>();
        services.AddSingleton<ISessionsClient, SeedSessionsClient>();
        services.AddSingleton<IRosterClient, SeedRosterClient>();
        services.AddSingleton<IStatsClient, SeedStatsClient>();
        services.AddSingleton<ILeaderboardClient, SeedLeaderboardClient>();
        services.AddSingleton<IProfileClient, SeedProfileClient>();
        return services;
#endif
    }

    private static bool IsSeedProviderAvailable
    {
        get
        {
#if RELEASE
            return false;
#else
            return true;
#endif
        }
    }
}
