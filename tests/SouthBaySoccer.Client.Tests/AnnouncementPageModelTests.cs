using FluentAssertions;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

public sealed class AnnouncementPageModelTests
{
    private static readonly Guid PrimaryGroupId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Appearing_WhenAdminIsInSeveralGroups_ResolvesThePrimaryAsTheOnlyAudience()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 27, 17, 0, 0, TimeSpan.Zero));
        var model = new AdminBroadcastPageModel(
            new SeedGroupsClient(),
            new SeedAnnouncementsClient(time),
            new StubNavigator(),
            time);

        await model.AppearingCommand.ExecuteAsync(null);

        // The seed links this player to three groups; only the primary may be broadcast to, and the
        // page model exposes no way to reach the other two.
        model.Group.Should().NotBeNull();
        model.Group!.Group.IsPrimary.Should().BeTrue();
        model.GroupName.Should().Be(model.Group.GroupName);
        model.PreviewGroupName.Should().Be(model.Group.GroupName);
        model.PushTitle.Should().Contain(model.Group.GroupName);
        model.AudienceLabel.Should().Be($"{model.Group.MemberCount} members");
        model.BroadcastLabel.Should().Be($"Broadcast to {model.Group.MemberCount} members");
    }

    [Fact]
    public async Task Send_WhenComposed_AddressesTheAdminsOwnGroupOnly()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 27, 17, 0, 0, TimeSpan.Zero));
        var groups = new SeedGroupsClient();
        var announcements = new SeedAnnouncementsClient(time);
        var model = new AdminBroadcastPageModel(groups, announcements, new StubNavigator(), time);
        await model.AppearingCommand.ExecuteAsync(null);
        var primary = (await groups.GetMyGroupsAsync(CancellationToken.None))
            .Groups.Single(group => group.IsPrimary);
        model.Body = "Kickoff moved to 10.";

        await model.SendCommand.ExecuteAsync(null);

        model.IsSent.Should().BeTrue();
        model.Group!.Id.Should().Be(primary.Id);
    }

    [Fact]
    public async Task Send_WhenSuccessful_LocksComposerAndPrependsRecentlySent()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 27, 17, 0, 0, TimeSpan.Zero));
        var model = new AdminBroadcastPageModel(
            new SeedGroupsClient(),
            new SeedAnnouncementsClient(time),
            new StubNavigator(),
            time);
        await model.AppearingCommand.ExecuteAsync(null);
        var previousFirst = model.RecentlySent[0];
        model.Body = new string('a', AdminBroadcastPageModel.MaximumBodyLength);

        await model.SendCommand.ExecuteAsync(null);

        model.IsSent.Should().BeTrue();
        model.IsComposerEnabled.Should().BeFalse();
        model.RecentlySent[0].Body.Should().Be(model.Body);
        model.RecentlySent[1].Should().Be(previousFirst);
    }

    [Fact]
    public async Task Feed_WhenUnreadFilterAndMarkRead_AppliesLocallyWithoutRefetch()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 27, 20, 0, 0, TimeSpan.Zero));
        var client = new SeedAnnouncementsClient(time);
        var model = new AnnouncementsPageModel(client, new StubNavigator(), new ClientResponseCache(time), time)
        {
            GroupId = PrimaryGroupId
        };
        await model.AppearingCommand.ExecuteAsync(null);

        model.ShowUnreadCommand.Execute(null);

        model.DayGroups.SelectMany(group => group).Should().OnlyContain(item => item.IsUnread);
        await model.MarkAllReadCommand.ExecuteAsync(null);
        model.UnreadCount.Should().Be(0);
        model.DayGroups.Should().BeEmpty();
    }

    private sealed class StubNavigator : IAnnouncementsNavigator
    {
        public Task GoToAnnouncementsAsync(Guid groupId) => Task.CompletedTask;
        public Task GoToAdminBroadcastAsync() => Task.CompletedTask;
        public Task GoBackAsync() => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
    }
}
