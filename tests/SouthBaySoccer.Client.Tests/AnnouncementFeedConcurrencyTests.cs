using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Announcements;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

public sealed class AnnouncementFeedConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_WhenOldGroupFinishesAfterNewGroup_DiscardsOldResultAndRetainsBusyState(bool oldFails)
    {
        var fixture = new Fixture();
        await fixture.Model.AppearingCommand.ExecuteAsync(null);
        var pagination = new TaskCompletionSource<AnnouncementFeedResponse>();
        var refresh = new TaskCompletionSource<AnnouncementFeedResponse>();
        fixture.Client.Setup(x => x.GetFeedAsync(fixture.A.GroupChatId, 20, It.IsNotNull<DateTime?>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).Returns(pagination.Task);
        fixture.Client.Setup(x => x.GetFeedAsync(fixture.A.GroupChatId, 20, null, null,
            It.IsAny<CancellationToken>())).Returns(refresh.Task);

        var pagingTask = fixture.Model.LoadMoreCommand.ExecuteAsync(null);
        var refreshTask = fixture.Model.RefreshCommand.ExecuteAsync(null);
        pagination.SetResult(fixture.Feed(fixture.A, "obsolete page", 7, false));
        await pagingTask;
        fixture.Model.CanChangeGroup.Should().BeFalse();
        fixture.Model.IsRefreshing.Should().BeTrue();
        // A programmatic navigation change must be safe even while the picker is disabled.
        fixture.Model.SelectedGroup = fixture.B;
        await (fixture.Model.RefreshCommand.ExecutionTask ?? Task.CompletedTask);
        fixture.Model.CanChangeGroup.Should().BeFalse();
        fixture.Model.IsRefreshing.Should().BeTrue();
        if (oldFails) refresh.SetException(new HttpRequestException("Old group failed"));
        else refresh.SetResult(fixture.Feed(fixture.A, "obsolete refresh", 7, true));
        await refreshTask;

        fixture.Model.GroupName.Should().Be("B");
        fixture.Model.UnreadCount.Should().Be(2);
        fixture.Model.DayGroups.SelectMany(x => x).Should().ContainSingle().Which.Body.Should().Be("B body");
        fixture.Model.HasMore.Should().BeFalse();
        fixture.Model.HasLoadMoreError.Should().BeFalse();
        fixture.Model.State.Should().Be(ViewState.Content);
        fixture.Model.CanChangeGroup.Should().BeTrue();
        fixture.Model.IsRefreshing.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarkAllRead_WhenGroupChangesBeforeCompletion_DoesNotChangeNewGroup(bool oldFails)
    {
        var fixture = new Fixture();
        await fixture.Model.AppearingCommand.ExecuteAsync(null);
        var mark = new TaskCompletionSource<MarkAnnouncementsReadResponse>();
        fixture.Client.Setup(x => x.MarkReadAsync(fixture.A.GroupChatId, It.IsAny<CancellationToken>())).Returns(mark.Task);

        var markTask = fixture.Model.MarkAllReadCommand.ExecuteAsync(null);
        fixture.Model.SelectedGroup = fixture.B;
        await (fixture.Model.RefreshCommand.ExecutionTask ?? Task.CompletedTask);
        fixture.Model.CanChangeGroup.Should().BeFalse();
        if (oldFails) mark.SetException(new HttpRequestException("Old mark failed"));
        else mark.SetResult(new(fixture.A.GroupChatId, Fixture.Now, 0));
        await markTask;

        fixture.Model.UnreadCount.Should().Be(2);
        fixture.Model.DayGroups.SelectMany(x => x).Should().ContainSingle().Which.IsUnread.Should().BeTrue();
        fixture.Model.GroupName.Should().Be("B");
        fixture.Model.State.Should().Be(ViewState.Content);
        fixture.Model.CanChangeGroup.Should().BeTrue();
        fixture.Client.Verify(x => x.MarkReadAsync(fixture.B.GroupChatId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refresh_WhenSameGroupMarkIsPending_PreservesSuccessfulReadResult()
    {
        var fixture = new Fixture();
        await fixture.Model.AppearingCommand.ExecuteAsync(null);
        var mark = new TaskCompletionSource<MarkAnnouncementsReadResponse>();
        fixture.Client.Setup(x => x.MarkReadAsync(fixture.A.GroupChatId, It.IsAny<CancellationToken>())).Returns(mark.Task);

        var markTask = fixture.Model.MarkAllReadCommand.ExecuteAsync(null);
        fixture.Model.IsRefreshing = true; // RefreshView sets its bound state before invoking the command.
        await fixture.Model.RefreshCommand.ExecuteAsync(null);
        fixture.Model.IsRefreshing.Should().BeFalse();
        mark.SetResult(new(fixture.A.GroupChatId, Fixture.Now, 0));
        await markTask;

        fixture.Model.UnreadCount.Should().Be(0);
        fixture.Model.DayGroups.SelectMany(x => x).Should().OnlyContain(x => !x.IsUnread);
        fixture.Client.Verify(x => x.GetFeedAsync(fixture.A.GroupChatId, 20, null, null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class Fixture
    {
        public static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        public GroupMembershipDto A { get; } = Membership("A");
        public GroupMembershipDto B { get; } = Membership("B");
        public Mock<IAnnouncementsClient> Client { get; } = new();
        public AnnouncementsPageModel Model { get; }
        public Fixture()
        {
            var groups = new Mock<IGroupsClient>();
            groups.Setup(x => x.GetMyMembershipsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MyGroupMembershipsResponse(false, true, [A, B]));
            Client.Setup(x => x.GetFeedAsync(A.GroupChatId, 20, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Feed(A, "A body", 1, true));
            Client.Setup(x => x.GetFeedAsync(B.GroupChatId, 20, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Feed(B, "B body", 2, false));
            Model = new(Client.Object, Mock.Of<IAnnouncementsNavigator>(), new ClientResponseCache(TimeProvider.System),
                TimeProvider.System, groups.Object) { GroupId = A.GroupChatId };
        }
        public AnnouncementFeedResponse Feed(GroupMembershipDto group, string body, int unread, bool more) =>
            new(group.GroupChatId, group.GroupName,
                [new(Guid.NewGuid(), group.GroupChatId, group.GroupName, "Admin", body, Now, true)], unread,
                more ? Now : null, more ? Guid.NewGuid() : null);
        private static GroupMembershipDto Membership(string name) => new(Guid.NewGuid(), name,
            GroupMembershipStatuses.Approved, GroupMemberRoles.Member, GroupMembershipSources.Request, Now, Now);
    }
}
