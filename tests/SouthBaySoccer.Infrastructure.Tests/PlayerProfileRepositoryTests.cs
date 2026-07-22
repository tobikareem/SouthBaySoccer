using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class PlayerProfileRepositoryTests(InfrastructureDatabaseFixture database)
{
    [Fact]
    public async Task ListDirectoryAsync_WhenProfilesExist_ReturnsSortedActiveProjection()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IPlayerProfileRepository>();
        var uniquePrefix = Guid.NewGuid().ToString("N");
        var zuri = CreatePlayer($"{uniquePrefix} Zuri Ade", "Forward", isGuest: false);
        var ada = CreatePlayer($"{uniquePrefix} Ada Okafor", "Midfielder", isGuest: true);
        var deleted = CreatePlayer($"{uniquePrefix} Deleted Player", "Goalkeeper", isGuest: false);
        await db.PlayerProfiles.AddRangeAsync(zuri, ada, deleted);
        await db.SaveChangesAsync();
        repository.SoftDelete(deleted);
        await db.SaveChangesAsync();

        var rows = await repository.ListDirectoryAsync();

        var seededRows = rows.Where(row => row.PlayerProfileId == ada.Id || row.PlayerProfileId == zuri.Id).ToArray();
        seededRows.Select(row => row.PlayerProfileId).Should().Equal(ada.Id, zuri.Id);
        rows.Should().NotContain(row => row.PlayerProfileId == deleted.Id);
        seededRows[0].DisplayName.Should().Be(ada.DisplayName);
        seededRows[0].PreferredPosition.Should().Be("Midfielder");
        seededRows[0].IsGuest.Should().BeTrue();
        seededRows[0].Matches.Should().Be(0);
    }

    private ServiceProvider CreateServiceProvider()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(candidate => candidate.UtcNow)
            .Returns(new DateTime(2026, 7, 22, 2, 0, 0, DateTimeKind.Utc));
        var services = new ServiceCollection();
        services.AddSingleton(clock.Object);
        services.AddInfrastructure(database.ConnectionString);
        return services.BuildServiceProvider();
    }

    private static PlayerProfile CreatePlayer(string displayName, string position, bool isGuest) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = displayName,
        NormalizedDisplayName = displayName.ToUpperInvariant(),
        PreferredPosition = position,
        IsGuest = isGuest,
        Role = PlayerRole.Player,
    };
}
