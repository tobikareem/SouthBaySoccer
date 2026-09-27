using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Announcements;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

public sealed class AnnouncementGroupAccessTests
{
    private static readonly DateTime SentAtUtc = new(2026, 9, 26, 17, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComposerAppearing_WhenMembershipsVary_OffersOnlyApprovedManagedGroups(bool isOwner)
    {
        var admin = Membership("Admin group", GroupMembershipStatuses.Approved, GroupMemberRoles.Admin);
        var ordinary = Membership("Member group", GroupMembershipStatuses.Approved, GroupMemberRoles.Member);
        var pending = Membership("Pending group", GroupMembershipStatuses.Pending, GroupMemberRoles.Admin);
        var removed = Membership("Removed group", GroupMembershipStatuses.Removed, GroupMemberRoles.Admin);
        var groups = Groups(isOwner, admin, ordinary, pending, removed);
        var announcements = Announcements();
        var model = new AdminBroadcastPageModel(groups.Object, announcements.Object,
            new Mock<IAnnouncementsNavigator>().Object, TimeProvider.System);

        await model.AppearingCommand.ExecuteAsync(null);

        model.State.Should().Be(ViewState.Content);
        model.Groups.Select(group => group.GroupChatId).Should().BeEquivalentTo(
            isOwner ? new[] { admin.GroupChatId, ordinary.GroupChatId } : new[] { admin.GroupChatId });
    }

    [Theory]
    [InlineData(GroupMembershipStatuses.Approved, GroupMemberRoles.Member)]
    [InlineData(GroupMembershipStatuses.Pending, GroupMemberRoles.Admin)]
    public async Task ComposerSend_WhenNoApprovedAdminMembership_DoesNotPost(string status, string role)
    {
        var groups = Groups(false, Membership("Group", status, role));
        var announcements = Announcements();
        var model = new AdminBroadcastPageModel(groups.Object, announcements.Object,
            new Mock<IAnnouncementsNavigator>().Object, TimeProvider.System);
        await model.AppearingCommand.ExecuteAsync(null);
        model.Body = "Kickoff at ten.";

        await model.SendCommand.ExecuteAsync(null);

        model.State.Should().Be(ViewState.Empty);
        model.CanSend.Should().BeFalse();
        announcements.Verify(client => client.PostAsync(It.IsAny<Guid>(), It.IsAny<PostAnnouncementRequest>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ComposerSend_WhenApprovedAdminPosts_SendsInAppOnly()
    {
        var admin = Membership("Admin group", GroupMembershipStatuses.Approved, GroupMemberRoles.Admin);
        var announcements = Announcements();
        announcements.Setup(client => client.PostAsync(admin.GroupChatId,
                It.Is<PostAnnouncementRequest>(request => request.Body == "Kickoff at ten." && !request.SendPush),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SentAnnouncementDto(Guid.NewGuid(), admin.GroupChatId, admin.GroupName,
                "Kickoff at ten.", SentAtUtc, 0, 12));
        var model = new AdminBroadcastPageModel(Groups(false, admin).Object, announcements.Object,
            new Mock<IAnnouncementsNavigator>().Object, TimeProvider.System);
        await model.AppearingCommand.ExecuteAsync(null);
        model.Body = "Kickoff at ten.";

        await model.SendCommand.ExecuteAsync(null);

        model.IsSent.Should().BeTrue();
        announcements.VerifyAll();
    }

    [Theory]
    [InlineData(GroupMemberRoles.Admin, false, true)]
    [InlineData(GroupMemberRoles.Member, false, false)]
    [InlineData(GroupMemberRoles.Member, true, true)]
    public async Task FeedAppearing_WhenMembershipsLoaded_OffersApprovedGroupsAndAppropriatePostAccess(
        string role, bool isOwner, bool expectedCanPost)
    {
        var approved = Membership("Approved group", GroupMembershipStatuses.Approved, role);
        var pending = Membership("Pending group", GroupMembershipStatuses.Pending, GroupMemberRoles.Admin);
        var removed = Membership("Removed group", GroupMembershipStatuses.Removed, GroupMemberRoles.Admin);
        var announcements = Announcements();
        SetupFeed(announcements, approved);
        var model = FeedModel(announcements, Groups(isOwner, approved, pending, removed));

        await model.AppearingCommand.ExecuteAsync(null);

        model.Groups.Should().ContainSingle().Which.Should().Be(approved);
        model.GroupId.Should().Be(approved.GroupChatId);
        model.CanPost.Should().Be(expectedCanPost);
        model.GroupName.Should().Be(approved.GroupName);
        model.DayGroups.SelectMany(group => group).Should().ContainSingle();
    }

    [Fact]
    public async Task FeedSelectedGroup_WhenChanged_LoadsSecondGroupsAnnouncementsAndPostAccess()
    {
        var admin = Membership("Admin group", GroupMembershipStatuses.Approved, GroupMemberRoles.Admin);
        var ordinary = Membership("Member group", GroupMembershipStatuses.Approved, GroupMemberRoles.Member);
        var announcements = Announcements();
        SetupFeed(announcements, admin);
        SetupFeed(announcements, ordinary);
        var model = FeedModel(announcements, Groups(false, admin, ordinary));
        await model.AppearingCommand.ExecuteAsync(null);
        model.CanPost.Should().BeTrue();

        model.SelectedGroup = model.Groups.Single(group => group.GroupChatId == ordinary.GroupChatId);
        if (model.RefreshCommand.ExecutionTask is { } refresh)
        {
            await refresh;
        }

        model.GroupId.Should().Be(ordinary.GroupChatId);
        model.GroupName.Should().Be(ordinary.GroupName);
        model.CanPost.Should().BeFalse();
        model.DayGroups.SelectMany(group => group).Should().ContainSingle()
            .Which.Body.Should().Be(ordinary.GroupName);
        announcements.Verify(client => client.GetFeedAsync(ordinary.GroupChatId, 20, null, null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FeedAppearing_WhenReopened_RefreshesMembershipAndFeed()
    {
        var admin = Membership("Admin group", GroupMembershipStatuses.Approved, GroupMemberRoles.Admin);
        var groups = Groups(false, admin);
        var announcements = Announcements();
        SetupFeed(announcements, admin);
        var cache = new Mock<IClientResponseCache>();
        var model = new AnnouncementsPageModel(announcements.Object, new Mock<IAnnouncementsNavigator>().Object,
            cache.Object, TimeProvider.System, groups.Object);

        await model.AppearingCommand.ExecuteAsync(null);
        await model.AppearingCommand.ExecuteAsync(null);

        groups.Verify(client => client.GetMyMembershipsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        announcements.Verify(client => client.GetFeedAsync(admin.GroupChatId, 20, null, null,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        cache.Verify(client => client.Invalidate("announcements:"), Times.Exactly(2));
        model.DayGroups.SelectMany(group => group).Should().ContainSingle();
    }

    [Fact]
    public async Task ComposerAppearing_WhenInitialGroupProvided_SelectsItUsingMembershipsOnly()
    {
        var first = Membership("First group", GroupMembershipStatuses.Approved, GroupMemberRoles.Admin);
        var second = Membership("Second group", GroupMembershipStatuses.Approved, GroupMemberRoles.Admin);
        var groups = Groups(false, first, second);
        var model = new AdminBroadcastPageModel(groups.Object, Announcements().Object,
            new Mock<IAnnouncementsNavigator>().Object, TimeProvider.System)
        {
            InitialGroupId = second.GroupChatId,
        };

        await model.AppearingCommand.ExecuteAsync(null);

        model.State.Should().Be(ViewState.Content);
        model.Group.Should().Be(second);
        groups.Verify(client => client.GetMyMembershipsAsync(It.IsAny<CancellationToken>()), Times.Once);
        groups.Verify(client => client.GetMyGroupsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ComposerAppearing_WhenRetryingFailedPost_PreservesSelectedGroupAndIdempotencyKey()
    {
        var first = Membership("First group", GroupMembershipStatuses.Approved, GroupMemberRoles.Admin);
        var second = Membership("Second group", GroupMembershipStatuses.Approved, GroupMemberRoles.Admin);
        var groups = Groups(false, first, second);
        var announcements = Announcements();
        var sentKeys = new List<string>();
        announcements.Setup(client => client.PostAsync(second.GroupChatId,
                It.Is<PostAnnouncementRequest>(request => request.Body == "Kickoff at ten." && !request.SendPush),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, PostAnnouncementRequest, string, CancellationToken>((_, _, key, _) => sentKeys.Add(key))
            .Returns(() => sentKeys.Count == 1
                ? Task.FromException<SentAnnouncementDto>(new HttpRequestException("Connection lost"))
                : Task.FromResult(new SentAnnouncementDto(Guid.NewGuid(), second.GroupChatId,
                    second.GroupName, "Kickoff at ten.", SentAtUtc, 0, 12)));
        var model = new AdminBroadcastPageModel(groups.Object, announcements.Object,
            new Mock<IAnnouncementsNavigator>().Object, TimeProvider.System);
        await model.AppearingCommand.ExecuteAsync(null);
        model.Group = model.Groups.Single(group => group.GroupChatId == second.GroupChatId);
        model.Body = "Kickoff at ten.";
        await model.SendCommand.ExecuteAsync(null);
        model.State.Should().Be(ViewState.Offline);

        await model.AppearingCommand.ExecuteAsync(null);
        await model.SendCommand.ExecuteAsync(null);

        model.Group.Should().Be(second);
        model.IsSent.Should().BeTrue();
        sentKeys.Should().HaveCount(2);
        sentKeys[1].Should().Be(sentKeys[0]);
    }

    [Fact]
    public async Task FeedCompose_WhenSecondAdminGroupSelected_NavigatesWithThatGroup()
    {
        var first = Membership("First group", GroupMembershipStatuses.Approved, GroupMemberRoles.Admin);
        var second = Membership("Second group", GroupMembershipStatuses.Approved, GroupMemberRoles.Admin);
        var announcements = Announcements();
        SetupFeed(announcements, first);
        SetupFeed(announcements, second);
        var navigator = new Mock<IAnnouncementsNavigator>(MockBehavior.Strict);
        navigator.Setup(client => client.GoToAdminBroadcastAsync(second.GroupChatId)).Returns(Task.CompletedTask);
        var model = new AnnouncementsPageModel(announcements.Object, navigator.Object,
            new Mock<IClientResponseCache>().Object, TimeProvider.System, Groups(false, first, second).Object);
        await model.AppearingCommand.ExecuteAsync(null);
        model.SelectedGroup = second;
        if (model.RefreshCommand.ExecutionTask is { } refresh)
        {
            await refresh;
        }

        await model.ComposeCommand.ExecuteAsync(null);

        navigator.Verify(client => client.GoToAdminBroadcastAsync(second.GroupChatId), Times.Once);
    }

    private static AnnouncementsPageModel FeedModel(Mock<IAnnouncementsClient> announcements, Mock<IGroupsClient> groups) =>
        new(announcements.Object, new Mock<IAnnouncementsNavigator>().Object,
            new Mock<IClientResponseCache>().Object, TimeProvider.System, groups.Object);

    private static GroupMembershipDto Membership(string name, string status, string role) =>
        new(Guid.NewGuid(), name, status, role, GroupMembershipSources.Request, SentAtUtc,
            status == GroupMembershipStatuses.Approved ? SentAtUtc : null);

    private static Mock<IGroupsClient> Groups(bool isOwner, params GroupMembershipDto[] memberships)
    {
        var groups = new Mock<IGroupsClient>(MockBehavior.Strict);
        groups.Setup(client => client.GetMyMembershipsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupMembershipsResponse(isOwner,
                memberships.Any(group => group.Status == GroupMembershipStatuses.Approved), memberships));
        return groups;
    }

    private static Mock<IAnnouncementsClient> Announcements()
    {
        var announcements = new Mock<IAnnouncementsClient>(MockBehavior.Strict);
        announcements.Setup(client => client.GetSentAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SentAnnouncementsResponse([]));
        return announcements;
    }

    private static void SetupFeed(Mock<IAnnouncementsClient> announcements, GroupMembershipDto group) =>
        announcements.Setup(client => client.GetFeedAsync(group.GroupChatId, 20, null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnnouncementFeedResponse(group.GroupChatId, group.GroupName,
                [new AnnouncementDto(Guid.NewGuid(), group.GroupChatId, group.GroupName, "Admin",
                    group.GroupName, SentAtUtc, true)], 1, null, null));
}
