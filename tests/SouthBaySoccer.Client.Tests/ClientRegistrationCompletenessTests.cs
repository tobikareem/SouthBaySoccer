using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;
using Xunit;

namespace SouthBaySoccer.Client.Tests;

/// <summary>
/// Every service the app resolves at startup must resolve under <em>both</em> data sources.
/// <para>
/// AuthenticationCoordinator is constructed during startup regardless of mode, but the session
/// refresher it depends on was registered only inside the API branch — so Seed builds threw
/// "Unable to resolve service" before a single page rendered. The unit tests never caught it
/// because they construct the coordinator directly with mocks; only container composition shows it.
/// </para>
/// </summary>
public sealed class ClientRegistrationCompletenessTests
{
    [Theory]
    [InlineData(ClientDataSource.Api)]
    [InlineData(ClientDataSource.Seed)]
    public void AddSouthBaySoccerClients_ForEitherDataSource_ResolvesTheStartupObjectGraph(
        ClientDataSource dataSource)
    {
        using var provider = BuildProvider(dataSource);

        // Resolving the coordinator walks its whole constructor graph, so this covers the session
        // refresher and anything else added to it later without needing a per-dependency assertion.
        var act = () => provider.GetRequiredService<IAuthenticationCoordinator>();

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(ClientDataSource.Api)]
    [InlineData(ClientDataSource.Seed)]
    public void AddSouthBaySoccerClients_ForEitherDataSource_ResolvesTheSessionRefresher(
        ClientDataSource dataSource)
    {
        using var provider = BuildProvider(dataSource);

        provider.GetService<IAuthenticationSessionRefresher>()
            .Should().NotBeNull("both data sources construct AuthenticationCoordinator at startup");
    }

    private static ServiceProvider BuildProvider(ClientDataSource dataSource)
    {
        var services = new ServiceCollection();
        var pickupPalOptions = new PickupPalOptions();
        // Mirrors MauiProgram: these are registered alongside the clients, not by
        // AddSouthBaySoccerClients, so the graph under test matches what startup actually builds.
        services.AddSingleton(pickupPalOptions);
        services.AddSingleton(Mock.Of<ISecureTokenStore>());
        services.AddSingleton(Mock.Of<IAuthenticationNavigator>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAuthenticationCoordinator, AuthenticationCoordinator>();

        services.AddSouthBaySoccerClients(
            new ClientDataSourceOptions { DataSource = dataSource },
            pickupPalOptions);

        return services.BuildServiceProvider();
    }
}
