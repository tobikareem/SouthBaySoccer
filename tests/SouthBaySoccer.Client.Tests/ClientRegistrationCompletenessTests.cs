using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.PageModels;
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
#if !RELEASE
    // Seed only exists in the configuration that ships it. Release compiles the seed clients out and
    // AddSouthBaySoccerClients rejects the Seed data source outright, so asserting it resolves there
    // would assert the opposite of the intended behaviour — see the Release-only case below.
    [InlineData(ClientDataSource.Seed)]
#endif
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
#if !RELEASE
    // Seed only exists in the configuration that ships it. Release compiles the seed clients out and
    // AddSouthBaySoccerClients rejects the Seed data source outright, so asserting it resolves there
    // would assert the opposite of the intended behaviour — see the Release-only case below.
    [InlineData(ClientDataSource.Seed)]
#endif
    public void AddSouthBaySoccerClients_ForEitherDataSource_ResolvesTheSessionRefresher(
        ClientDataSource dataSource)
    {
        using var provider = BuildProvider(dataSource);

        provider.GetService<IAuthenticationSessionRefresher>()
            .Should().NotBeNull("both data sources construct AuthenticationCoordinator at startup");
    }

    /// <summary>
    /// Resolves the page models reached by navigation rather than at startup. The startup graph
    /// assertions above never touch these, so a dependency registered for only one data source stays
    /// invisible until a user taps through to the page — which surfaces as a crash, not an error
    /// state, because Shell route construction has no exception boundary.
    /// </summary>
    [Theory]
    [InlineData(ClientDataSource.Api)]
#if !RELEASE
    [InlineData(ClientDataSource.Seed)]
#endif
    public void AddSouthBaySoccerClients_ForEitherDataSource_ResolvesNavigablePageModels(
        ClientDataSource dataSource)
    {
        using var provider = BuildProvider(dataSource);

        var act = () =>
        {
            _ = ActivatorUtilities.CreateInstance<AdminBroadcastPageModel>(provider);
            _ = ActivatorUtilities.CreateInstance<AnnouncementsPageModel>(provider);
            _ = ActivatorUtilities.CreateInstance<SchedulePageModel>(provider);
        };

        act.Should().NotThrow();
    }

#if RELEASE
    [Fact]
    public void AddSouthBaySoccerClients_SeedInRelease_IsRejected()
    {
        // The other half of the contract: a Release build must refuse the Seed data source rather
        // than half-wire it, since the seed clients are not compiled in.
        var act = () => BuildProvider(ClientDataSource.Seed);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Seed*Release*");
    }
#endif

    private static ServiceProvider BuildProvider(ClientDataSource dataSource)
    {
        var services = new ServiceCollection();
        var pickupPalOptions = new PickupPalOptions();
        // Mirrors MauiProgram: these are registered alongside the clients, not by
        // AddSouthBaySoccerClients, so the graph under test matches what startup actually builds.
        services.AddSingleton(pickupPalOptions);
        services.AddSingleton(Mock.Of<ISecureTokenStore>());
        services.AddSingleton(Mock.Of<IAuthenticationNavigator>());
        services.AddSingleton(Mock.Of<IAnnouncementsNavigator>());
        services.AddSingleton(Mock.Of<ISessionsNavigator>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAuthenticationCoordinator, AuthenticationCoordinator>();

        services.AddSouthBaySoccerClients(
            new ClientDataSourceOptions { DataSource = dataSource },
            pickupPalOptions);

        return services.BuildServiceProvider();
    }
}
