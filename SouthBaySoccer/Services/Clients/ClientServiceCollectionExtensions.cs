using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients.Caching;

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
        // Registered for both data sources: AuthenticationCoordinator depends on the cache
        // regardless of mode, so registering it only for API mode breaks Seed startup.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClientResponseCache, ClientResponseCache>();
        ClientDataSourceValidator.Validate(options, IsSeedProviderAvailable);

        return options.DataSource switch
        {
            ClientDataSource.Seed => AddSeedClients(services),
            ClientDataSource.Api => AddApiClients(services, pickupPalOptions),
            _ => throw new InvalidOperationException(
                $"Unsupported client data source '{options.DataSource}'.")
        };
    }

    // Shared config for every typed API client. The explicit timeout matters: HttpClient's default
    // is 100 seconds, which reads as a frozen button when the backend is unreachable or cold-starting.
    private static void ConfigureApiClient(HttpClient client, PickupPalOptions pickupPalOptions)
    {
        client.BaseAddress = pickupPalOptions.ApiBaseUri;
        client.Timeout = TimeSpan.FromSeconds(30);
    }

    private static IServiceCollection AddApiClients(IServiceCollection services, PickupPalOptions pickupPalOptions)
    {
        // ApiSessionsClient formats display labels in device-local time from a TimeProvider; the
        // app registers TimeProvider.System in MauiProgram, and TryAdd keeps bare test hosts working.
        services.AddTransient<CorrelationIdHandler>();
        services.AddTransient<AuthenticationHandler>();
        services.AddTransient<ApiExceptionHandler>();

        services.AddHttpClient(
            "SouthBaySoccer.Anonymous",
            client => ConfigureApiClient(client, pickupPalOptions))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddHttpMessageHandler<ApiExceptionHandler>();

        services.AddHttpClient<ApiProfileClient>(
            client => ConfigureApiClient(client, pickupPalOptions))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddHttpMessageHandler<AuthenticationHandler>()
            .AddHttpMessageHandler<ApiExceptionHandler>();
        services.AddTransient<IProfileClient>(provider => new CachedProfileClient(
            provider.GetRequiredService<ApiProfileClient>(),
            provider.GetRequiredService<IClientResponseCache>()));

        services.AddHttpClient<ISessionAdminClient, ApiSessionAdminClient>(
            client => ConfigureApiClient(client, pickupPalOptions))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddHttpMessageHandler<AuthenticationHandler>()
            .AddHttpMessageHandler<ApiExceptionHandler>();

        services.AddHttpClient<ApiPlayersClient>(
            client => ConfigureApiClient(client, pickupPalOptions))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddHttpMessageHandler<AuthenticationHandler>()
            .AddHttpMessageHandler<ApiExceptionHandler>();
        services.AddTransient<IPlayersClient>(provider => new CachedPlayersClient(
            provider.GetRequiredService<ApiPlayersClient>(),
            provider.GetRequiredService<IClientResponseCache>()));

        // Registered by concrete type so the caching decorator owns the ISessionsClient
        // registration; the handler pipeline below is unchanged and every real request still
        // flows through correlation -> auth -> exception handling.
        services.AddHttpClient<ApiSessionsClient>(
            client => ConfigureApiClient(client, pickupPalOptions))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddHttpMessageHandler<AuthenticationHandler>()
            .AddHttpMessageHandler<ApiExceptionHandler>();
        services.AddTransient<ISessionsClient>(provider => new CachedSessionsClient(
            provider.GetRequiredService<ApiSessionsClient>(),
            provider.GetRequiredService<IClientResponseCache>()));

        services.AddHttpClient<ApiRosterClient>(
            client => ConfigureApiClient(client, pickupPalOptions))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddHttpMessageHandler<AuthenticationHandler>()
            .AddHttpMessageHandler<ApiExceptionHandler>();
        services.AddTransient<IRosterClient>(provider => new CachedRosterClient(
            provider.GetRequiredService<ApiRosterClient>(),
            provider.GetRequiredService<IClientResponseCache>()));

        services.AddHttpClient<IStatsClient, ApiStatsClient>(
            client => ConfigureApiClient(client, pickupPalOptions))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddHttpMessageHandler<AuthenticationHandler>()
            .AddHttpMessageHandler<ApiExceptionHandler>();

        services.AddHttpClient<ILeaderboardClient, ApiLeaderboardClient>(
            client => ConfigureApiClient(client, pickupPalOptions))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddHttpMessageHandler<AuthenticationHandler>()
            .AddHttpMessageHandler<ApiExceptionHandler>();

        services.AddHttpClient<IGameDayClient, ApiGameDayClient>(
            client => ConfigureApiClient(client, pickupPalOptions))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddHttpMessageHandler<AuthenticationHandler>()
            .AddHttpMessageHandler<ApiExceptionHandler>();

        services.AddHttpClient<ApiGroupsClient>(
            client => ConfigureApiClient(client, pickupPalOptions))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddHttpMessageHandler<AuthenticationHandler>()
            .AddHttpMessageHandler<ApiExceptionHandler>();
        services.AddTransient<IGroupsClient>(provider => new CachedGroupsClient(
            provider.GetRequiredService<ApiGroupsClient>(),
            provider.GetRequiredService<IClientResponseCache>()));

        services.AddSingleton<IAuthenticationClient>(provider =>
            new AuthenticationClient(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("SouthBaySoccer.Anonymous"),
                pickupPalOptions));
        services.AddSingleton<IAuthenticationSessionRefresher, AuthenticationSessionRefresher>();

#if RELEASE
        return services;
#else
        return AddSeedClientsExceptApiBacked(services);
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
        services.AddSingleton<IProfileClient, SeedProfileClient>();
        return AddSeedClientsExceptProfileAndAuthentication(services);
#endif
    }

#if !RELEASE
    private static IServiceCollection AddSeedClientsExceptProfileAndAuthentication(IServiceCollection services)
    {
        AddSharedSeedClients(services);
        services.AddSingleton<ISessionAdminClient, SeedSessionAdminClient>();
        return services;
    }

    private static IServiceCollection AddSeedClientsExceptApiBacked(IServiceCollection services)
    {
        AddSharedSeedClients(services);
        return services;
    }

    private static void AddSharedSeedClients(IServiceCollection services)
    {
        services.TryAddSingleton<SeedState>();
        services.TryAddSingleton<SeedGameDayState>();
        services.TryAddSingleton<ISessionsClient, SeedSessionsClient>();
        services.TryAddSingleton<IRosterClient, SeedRosterClient>();
        services.TryAddSingleton<IStatsClient, SeedStatsClient>();
        services.TryAddSingleton<ILeaderboardClient, SeedLeaderboardClient>();
        services.TryAddSingleton<IPlayersClient, SeedPlayersClient>();
        services.TryAddSingleton<IGameDayClient, SeedGameDayClient>();
        services.TryAddSingleton<IGroupsClient, SeedGroupsClient>();
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
