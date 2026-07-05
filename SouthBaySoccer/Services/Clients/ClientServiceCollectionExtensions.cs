using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            ClientDataSource.Api => AddApiClients(services, pickupPalOptions),
            _ => throw new InvalidOperationException(
                $"Unsupported client data source '{options.DataSource}'.")
        };
    }

    private static IServiceCollection AddApiClients(IServiceCollection services, PickupPalOptions pickupPalOptions)
    {
        services.AddSingleton<IAuthenticationClient>(_ =>
            new AuthenticationClient(
                new HttpClient { BaseAddress = pickupPalOptions.ApiBaseUri },
                pickupPalOptions));

#if RELEASE
        return services;
#else
        return AddSeedClientsExceptAuthentication(services);
#endif
    }

    private static IServiceCollection AddSeedClients(IServiceCollection services)
    {
#if RELEASE
        throw new InvalidOperationException(
            "ClientDataSource 'Seed' is unavailable in Release builds.");
#else
        services.AddSingleton<SeedState>();
        services.AddSingleton<SeedGameDayState>();
        services.AddSingleton<IAuthenticationClient, SeedAuthenticationClient>();
        return AddSeedClientsExceptAuthentication(services);
#endif
    }

#if !RELEASE
    private static IServiceCollection AddSeedClientsExceptAuthentication(IServiceCollection services)
    {
        services.TryAddSingleton<SeedState>();
        services.TryAddSingleton<SeedGameDayState>();
        services.AddSingleton<ISessionsClient, SeedSessionsClient>();
        services.AddSingleton<ISessionAdminClient, SeedSessionAdminClient>();
        services.AddSingleton<IRosterClient, SeedRosterClient>();
        services.AddSingleton<IStatsClient, SeedStatsClient>();
        services.AddSingleton<ILeaderboardClient, SeedLeaderboardClient>();
        services.AddSingleton<IPlayersClient, SeedPlayersClient>();
        services.AddSingleton<IProfileClient, SeedProfileClient>();
        services.AddSingleton<IGameDayClient, SeedGameDayClient>();
        return services;
    }
#endif

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
