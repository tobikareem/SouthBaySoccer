using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Infrastructure.Authentication;
using SouthBaySoccer.Infrastructure.Identity;
using SouthBaySoccer.Infrastructure.Repositories;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class UserActivityRepositoryTests(InfrastructureDatabaseFixture database)
{
    [Fact]
    public async Task ReadPageAsync_WhenHistoryAndMembershipsExist_UsesStablePagingAndFullRecordedSummary()
    {
        await using var db = database.CreateDbContext();
        var identity = new ApplicationIdentityUser { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N") };
        var player = Player(identity.Id);
        db.Users.Add(identity);
        db.PlayerProfiles.Add(player);
        var firstAt = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var prefix = Guid.NewGuid().ToString("D")[..24];
        var first = Activity(identity.Id, player.Id, firstAt, UserActivityType.SignUp);
        var second = Activity(identity.Id, player.Id, firstAt.AddHours(1), UserActivityType.SignIn);
        var third = Activity(identity.Id, player.Id, firstAt.AddHours(1), UserActivityType.SignIn);
        second.Id = Guid.Parse(prefix + "000000000001");
        third.Id = Guid.Parse(prefix + "000000000002");
        db.UserActivities.AddRange(first, second, third);
        var group = new GroupChat { Id = Guid.NewGuid(), ExternalId = Guid.NewGuid().ToString("N"), GroupName = "Current group", Status = "SUBSCRIBED" };
        db.GroupChats.Add(group);
        var membership = new PlayerGroupLink
        {
            Id = Guid.NewGuid(), GroupChatId = group.Id, PlayerProfileId = player.Id,
            Status = GroupMembershipStatus.Pending, RequestedAtUtc = firstAt,
        };
        db.PlayerGroupLinks.Add(membership);
        await db.SaveChangesAsync();
        membership.Status = GroupMembershipStatus.Approved;
        await db.SaveChangesAsync();
        var repository = new UserActivityRepository(db);

        var pageOne = await repository.ReadPageAsync(1, 1);
        var pageTwo = await repository.ReadPageAsync(2, 1);

        pageOne.HasMore.Should().BeTrue();
        pageOne.Items.Should().ContainSingle().Which.Id.Should().Be(third.Id);
        pageTwo.Items.Should().ContainSingle().Which.Id.Should().Be(second.Id);
        var row = pageOne.Items.Single();
        row.FirstRecordedActivityAtUtc.Should().Be(firstAt);
        row.LastRecordedActivityAtUtc.Should().Be(firstAt.AddHours(1));
        row.SignInCount.Should().Be(2, "signup is not another sign-in and history is not page-limited");
        row.Groups.Should().ContainSingle().Which.Status.Should().Be("Approved");
        row.Groups.Single().GroupName.Should().Be("Current group");
        pageOne.TrackingStartedAtUtc.Should().NotBeNull();
        pageOne.TrackingStartedAtUtc.Should().BeOnOrBefore(firstAt);

        player.IsDeleted = true;
        await db.SaveChangesAsync();
        var afterDeletion = await repository.ReadPageAsync(1, 100);
        afterDeletion.Items.Should().NotContain(item => item.PlayerProfileId == player.Id);
        (await db.UserActivities.CountAsync(item => item.PlayerProfileId == player.Id)).Should().Be(3);
    }

    [Fact]
    public async Task IssueTokensAsync_WhenEventWriteFails_RollsBackInitialTokenAndEvent()
    {
        await using var db = database.CreateDbContext();
        var identity = new ApplicationIdentityUser { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N") };
        var player = Player(identity.Id);
        db.Users.Add(identity);
        db.PlayerProfiles.Add(player);
        await db.SaveChangesAsync();
        var subject = new AuthenticationTokenSubject(identity.Id, player.Id, ["Player"]) { ActivityType = (UserActivityType)99 };
        var issuer = AuthenticationTokenIssuerActivityTests.CreateIssuer(db);

        Func<Task> action = () => issuer.IssueTokensAsync(subject);
        await action.Should().ThrowAsync<DbUpdateException>();

        await using var assertionDb = database.CreateDbContext();
        (await assertionDb.RefreshTokens.CountAsync(token => token.IdentityUserId == identity.Id)).Should().Be(0);
        (await assertionDb.UserActivities.CountAsync(activity => activity.IdentityUserId == identity.Id)).Should().Be(0);
    }

    [Fact]
    public async Task RotateAsync_WhenInitialSessionExists_DoesNotCreateAnotherActivity()
    {
        await using var db = database.CreateDbContext();
        var identity = new ApplicationIdentityUser { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N") };
        var player = Player(identity.Id);
        db.Users.Add(identity);
        db.PlayerProfiles.Add(player);
        await db.SaveChangesAsync();
        var tokens = await AuthenticationTokenIssuerActivityTests.CreateIssuer(db)
            .IssueTokensAsync(new AuthenticationTokenSubject(identity.Id, player.Id, ["Player"]));
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(AuthenticationTokenIssuerActivityTests.Now.AddMinutes(1));
        var secrets = new Mock<IRefreshTokenSecretGenerator>();
        secrets.Setup(x => x.CreateToken()).Returns(Guid.NewGuid().ToString("N"));
        var exchange = new RefreshTokenExchangeService(db, clock.Object, new RefreshTokenHasher(), secrets.Object);

        var result = await exchange.RotateAsync(new RefreshTokenExchangeRequest(tokens.RefreshToken, null, null, null));

        result.Succeeded.Should().BeTrue();
        (await db.UserActivities.CountAsync(activity => activity.PlayerProfileId == player.Id)).Should().Be(1);
    }

    private static PlayerProfile Player(Guid identityId) => new()
    {
        Id = Guid.NewGuid(), IdentityUserId = identityId, DisplayName = "Recorded player",
        NormalizedDisplayName = "RECORDED PLAYER", PreferredPosition = "Forward", Role = PlayerRole.Player,
    };

    private static UserActivity Activity(Guid identityId, Guid playerId, DateTime at, UserActivityType type) => new()
    {
        Id = Guid.NewGuid(), IdentityUserId = identityId, PlayerProfileId = playerId,
        ActivityType = type, OccurredAtUtc = at, SessionFamilyId = Guid.NewGuid(), CreatedAt = at,
    };
}
