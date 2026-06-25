using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public class SeedClientRegistrationTests
{
    [Fact]
#if RELEASE
    public void AddSouthBaySoccerClients_SeedSelected_FailsFastBecauseProviderIsExcluded()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSouthBaySoccerClients(
            new ClientDataSourceOptions { DataSource = ClientDataSource.Seed },
            new PickupPalOptions());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Seed*Release*");
    }
#else
    public void AddSouthBaySoccerClients_SeedSelected_ResolvesCompleteApplicationScopedSet()
    {
        var services = new ServiceCollection();

        services.AddSouthBaySoccerClients(
            new ClientDataSourceOptions { DataSource = ClientDataSource.Seed },
            new PickupPalOptions());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAuthenticationClient>()
            .Should().BeOfType<SeedAuthenticationClient>();
        provider.GetRequiredService<ISessionsClient>().Should().BeOfType<SeedSessionsClient>();
        provider.GetRequiredService<ISessionAdminClient>().Should().BeOfType<SeedSessionAdminClient>();
        provider.GetRequiredService<IRosterClient>().Should().BeOfType<SeedRosterClient>();
        provider.GetRequiredService<IStatsClient>().Should().BeOfType<SeedStatsClient>();
        provider.GetRequiredService<ILeaderboardClient>()
            .Should().BeOfType<SeedLeaderboardClient>();
        provider.GetRequiredService<IPlayersClient>().Should().BeOfType<SeedPlayersClient>();
        provider.GetRequiredService<IProfileClient>().Should().BeOfType<SeedProfileClient>();
        provider.GetRequiredService<SeedState>()
            .Should().BeSameAs(provider.GetRequiredService<SeedState>());
    }
#endif

    [Fact]
    public void Validate_ApiSelected_FailsFastAndNamesMissingRegistrations()
    {
        var options = new ClientDataSourceOptions { DataSource = ClientDataSource.Api };

        var act = () => ClientDataSourceValidator.Validate(options, seedProviderAvailable: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IAuthenticationClient*ISessionsClient*ISessionAdminClient*IRosterClient*IStatsClient*ILeaderboardClient*IPlayersClient*IProfileClient*");
    }

    [Fact]
    public void Validate_SeedSelectedWithoutCompiledProvider_FailsFastForRelease()
    {
        var options = new ClientDataSourceOptions { DataSource = ClientDataSource.Seed };

        var act = () => ClientDataSourceValidator.Validate(options, seedProviderAvailable: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Seed*Release*");
    }
}
