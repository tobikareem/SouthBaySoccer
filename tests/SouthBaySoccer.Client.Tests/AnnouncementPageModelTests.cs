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
    public async Task SelectGroup_WhenAudienceChanges_UpdatesAllAudienceDependentTextTogether()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 27, 17, 0, 0, TimeSpan.Zero));
        var model = new AdminBroadcastPageModel(
            new SeedGroupsClient(),
            new SeedAnnouncementsClient(time),
            new StubNavigator(),
            time);
        await model.AppearingCommand.ExecuteAsync(null);
        var second = model.Groups[1];

        model.SelectGroupCommand.Execute(second);

        model.SelectedGroup.Should().BeSameAs(second);
        model.PreviewGroupName.Should().Be(second.GroupName);
        model.PushTitle.Should().Contain(second.GroupName);
        model.BroadcastLabel.Should().Be($"Broadcast to {second.MemberCount} members");
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
