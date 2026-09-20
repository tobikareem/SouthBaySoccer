using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class PlayerGroupLinkRepositoryTests(InfrastructureDatabaseFixture database)
{
    [Fact]
    public async Task ListApprovedPlayerIdsAsync_WhenMembershipsVary_ReturnsOnlyRequestedActiveApprovedMembersOfGroup()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IPlayerGroupLinkRepository>();
        var group = new GroupChat { ExternalId = $"{Guid.NewGuid():N}@g.us", GroupName = "Test group" };
        var other = new GroupChat { ExternalId = $"{Guid.NewGuid():N}@g.us", GroupName = "Other group" };
        var statuses = Enum.GetValues<GroupMembershipStatus>();
        var players = Enumerable.Range(0, statuses.Length + 3)
            .Select(index => new PlayerProfile { DisplayName = $"Player {index}", NormalizedDisplayName = $"PLAYER {index}" })
            .ToArray();
        db.GroupChats.AddRange(group, other);
        db.PlayerProfiles.AddRange(players);
        for (var index = 0; index < statuses.Length; index++)
        {
            db.PlayerGroupLinks.Add(new PlayerGroupLink { PlayerProfileId = players[index].Id, GroupChatId = group.Id, Status = statuses[index] });
        }

        var deleted = new PlayerGroupLink { PlayerProfileId = players[^3].Id, GroupChatId = group.Id, Status = GroupMembershipStatus.Approved };
        db.PlayerGroupLinks.AddRange(deleted,
            new PlayerGroupLink { PlayerProfileId = players[^2].Id, GroupChatId = other.Id, Status = GroupMembershipStatus.Approved },
            new PlayerGroupLink { PlayerProfileId = players[^1].Id, GroupChatId = group.Id, Status = GroupMembershipStatus.Approved });
        await db.SaveChangesAsync();
        repository.SoftDelete(deleted);
        await db.SaveChangesAsync();

        var result = await repository.ListApprovedPlayerIdsAsync(group.Id, players[..^1].Select(player => player.Id).ToArray());

        result.Should().Equal(players[Array.IndexOf(statuses, GroupMembershipStatus.Approved)].Id);
    }

    [Fact]
    public async Task ListApprovedPlayerIdsAsync_WhenNoCandidates_ReturnsEmpty()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPlayerGroupLinkRepository>();

        var result = await repository.ListApprovedPlayerIdsAsync(Guid.NewGuid(), []);

        result.Should().BeEmpty();
    }

    private ServiceProvider CreateServiceProvider()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 9, 19, 18, 0, 0, DateTimeKind.Utc));
        var services = new ServiceCollection();
        services.AddSingleton(clock.Object);
        services.AddInfrastructure(database.ConnectionString);
        return services.BuildServiceProvider();
    }
}
