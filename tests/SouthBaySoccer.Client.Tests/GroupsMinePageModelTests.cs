using System.Net.Http;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

public class GroupsMinePageModelTests
{
    private static readonly Guid BayAreaId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid MorningId = Guid.Parse("50000000-0000-0000-0000-000000000002");
    private static readonly Guid SaturdayId = Guid.Parse("50000000-0000-0000-0000-000000000003");
    private static readonly DateTime RequestedAtUtc = new(2026, 6, 3, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Appearing_SplitsCatalogIntoMembershipsAndJoinableGroups_WithStatusPills()
    {
        var groups = GroupsReturning(
            [
                Catalog(BayAreaId, "Bay Area Soccer", 349, GroupMembershipStatuses.Approved, GroupMemberRoles.Admin),
                Catalog(MorningId, "Morning Pick Up Soccer", 67, GroupMembershipStatuses.Pending),
                Catalog(SaturdayId, "Saturday Soccer", 58, GroupMembershipStatuses.None),
            ],
            [
                Membership(BayAreaId, "Bay Area Soccer", GroupMembershipStatuses.Approved, GroupMemberRoles.Admin),
                Membership(MorningId, "Morning Pick Up Soccer", GroupMembershipStatuses.Pending, GroupMemberRoles.Member),
            ]);
        var pageModel = CreatePageModel(groups);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Memberships.Should().SatisfyRespectively(
            bayArea =>
            {
                bayArea.Name.Should().Be("Bay Area Soccer");
                bayArea.StatusVariant.Should().Be(BadgeVariant.Success);
                bayArea.IsAdmin.Should().BeTrue();
                bayArea.Detail.Should().Be("Admin · 349 members");
                bayArea.LeaveActionText.Should().Be("Leave");
            },
            morning =>
            {
                morning.StatusVariant.Should().Be(BadgeVariant.Warning);
                morning.Detail.Should().StartWith("Requested ").And.EndWith(" · 67 members");
                morning.LeaveActionText.Should().Be("Cancel");
                morning.CanLeave.Should().BeTrue();
            });
        pageModel.JoinableGroups.Select(group => group.Name).Should().Equal("Saturday Soccer");
        pageModel.HasNoMemberships.Should().BeFalse();
    }

    [Fact]
    public async Task Request_SendsThatGroupId_InvalidatesGroupsCache_AndReloads()
    {
        var cache = new Mock<IClientResponseCache>();
        var groups = GroupsReturning([Catalog(SaturdayId, "Saturday Soccer", 58, GroupMembershipStatuses.None)], []);
        groups.Setup(x => x.RequestMembershipsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupMembershipsResponse(false, false, []));
        var pageModel = CreatePageModel(groups, cache: cache);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.RequestCommand.ExecuteAsync(pageModel.JoinableGroups[0]);

        groups.Verify(x => x.RequestMembershipsAsync(
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == SaturdayId), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(x => x.Invalidate("groups:"), Times.Once);
        groups.Verify(x => x.GetCatalogAsync(It.IsAny<CancellationToken>()), Times.Exactly(2), "the lists reload after the write");
        pageModel.HasActionMessage.Should().BeFalse();
    }

    [Fact]
    public async Task Appearing_WithdrawnRequest_OffersTheGroupForRequestAgain()
    {
        var groups = GroupsReturning(
            [Catalog(SaturdayId, "Saturday Soccer", 58, GroupMembershipStatuses.Withdrawn)],
            [Membership(SaturdayId, "Saturday Soccer", GroupMembershipStatuses.Withdrawn, GroupMemberRoles.Member)]);
        groups.Setup(x => x.RequestMembershipsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupMembershipsResponse(false, false, []));
        var pageModel = CreatePageModel(groups);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.RequestCommand.ExecuteAsync(pageModel.JoinableGroups.Single());

        pageModel.Memberships.Should().BeEmpty();
        groups.Verify(x => x.RequestMembershipsAsync(
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == SaturdayId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Leave_WhenConfirmed_CallsLeaveInvalidatesAndReloads()
    {
        var cache = new Mock<IClientResponseCache>();
        var dialogs = DialogsAnswering(true);
        var groups = GroupsReturning(
            [Catalog(BayAreaId, "Bay Area Soccer", 349, GroupMembershipStatuses.Approved)],
            [Membership(BayAreaId, "Bay Area Soccer", GroupMembershipStatuses.Approved, GroupMemberRoles.Member)]);
        var pageModel = CreatePageModel(groups, dialogs, cache);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.LeaveCommand.ExecuteAsync(pageModel.Memberships[0]);

        groups.Verify(x => x.LeaveAsync(BayAreaId, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(x => x.Invalidate("groups:"), Times.Once);
        groups.Verify(x => x.GetCatalogAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Leave_WhenDeclinedInDialog_DoesNothing()
    {
        var groups = GroupsReturning(
            [Catalog(BayAreaId, "Bay Area Soccer", 349, GroupMembershipStatuses.Approved)],
            [Membership(BayAreaId, "Bay Area Soccer", GroupMembershipStatuses.Approved, GroupMemberRoles.Member)]);
        var pageModel = CreatePageModel(groups, DialogsAnswering(false));
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.LeaveCommand.ExecuteAsync(pageModel.Memberships[0]);

        groups.Verify(x => x.LeaveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Request_WhenClientThrows_ShowsRecoverableMessageAndKeepsLists()
    {
        var groups = GroupsReturning([Catalog(SaturdayId, "Saturday Soccer", 58, GroupMembershipStatuses.None)], []);
        groups.Setup(x => x.RequestMembershipsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var pageModel = CreatePageModel(groups);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.RequestCommand.ExecuteAsync(pageModel.JoinableGroups[0]);

        pageModel.ActionMessage.Should().Be(GroupsMinePageModel.RequestFailedMessage);
        pageModel.State.Should().Be(ViewState.Content);
        pageModel.JoinableGroups.Should().ContainSingle();
        pageModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Appearing_WhenOffline_ShowsOfflineState()
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(x => x.GetCatalogAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("offline"));
        groups.Setup(x => x.GetMyMembershipsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupMembershipsResponse(false, false, []));
        var pageModel = CreatePageModel(groups);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Offline);
        pageModel.StateTitle.Should().Be(GroupsMinePageModel.OfflineTitle);
    }

    private static GroupsMinePageModel CreatePageModel(
        Mock<IGroupsClient> groups,
        Mock<IUserDialogService>? dialogs = null,
        Mock<IClientResponseCache>? cache = null) =>
        new(
            groups.Object,
            new Mock<IProfileNavigator>().Object,
            (dialogs ?? DialogsAnswering(true)).Object,
            cache?.Object ?? new ClientResponseCache(TimeProvider.System));

    private static Mock<IGroupsClient> GroupsReturning(
        IReadOnlyList<GroupWithMembershipDto> catalog,
        IReadOnlyList<GroupMembershipDto> memberships)
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(x => x.GetCatalogAsync(It.IsAny<CancellationToken>())).ReturnsAsync(catalog);
        groups.Setup(x => x.GetMyMembershipsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupMembershipsResponse(false, memberships.Any(m => m.Status == GroupMembershipStatuses.Approved), memberships));
        groups.Setup(x => x.LeaveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return groups;
    }

    private static Mock<IUserDialogService> DialogsAnswering(bool confirmed)
    {
        var dialogs = new Mock<IUserDialogService>();
        dialogs.Setup(x => x.ShowConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(confirmed);
        return dialogs;
    }

    private static GroupWithMembershipDto Catalog(Guid id, string name, int members, string status, string role = GroupMemberRoles.Member) =>
        new(id, name, members, status, role, PendingRequestCount: 0);

    private static GroupMembershipDto Membership(Guid id, string name, string status, string role) =>
        new(id, name, status, role, GroupMembershipSources.Request, RequestedAtUtc, null);
}
