using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

public class LinkGroupPageModelTests
{
    private static readonly Guid BayAreaId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid MorningId = Guid.Parse("50000000-0000-0000-0000-000000000002");
    private static readonly Guid SaturdayId = Guid.Parse("50000000-0000-0000-0000-000000000003");

    private static readonly GroupWithMembershipDto BayArea = Catalog(BayAreaId, "Bay Area Soccer", 349, GroupMembershipStatuses.None);
    private static readonly GroupWithMembershipDto Morning = Catalog(MorningId, "Morning Pick Up Soccer", 67, GroupMembershipStatuses.None);
    private static readonly GroupWithMembershipDto Saturday = Catalog(SaturdayId, "Saturday Soccer", 58, GroupMembershipStatuses.None);

    [Fact]
    public async Task Appearing_WithGroups_ShowsContentAndBlocksContinueUntilSomethingIsSelected()
    {
        var groups = GroupsReturning(BayArea, Morning);
        var pageModel = CreatePageModel(groups);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.IsChoosing.Should().BeTrue();
        pageModel.Groups.Should().HaveCount(2);
        pageModel.CanContinue.Should().BeFalse("no group is selected yet");
        pageModel.ContinueCommand.CanExecute(null).Should().BeFalse();
        pageModel.SelectedCountText.Should().Be("0 selected");
    }

    [Fact]
    public async Task SelectedGroups_MultipleAdded_EnablesContinueAndMirrorsRowSelection()
    {
        var pageModel = CreatePageModel(GroupsReturning(BayArea, Morning, Saturday));
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.SelectedGroups.Add(pageModel.Groups[0]);
        pageModel.SelectedGroups.Add(pageModel.Groups[2]);

        pageModel.CanContinue.Should().BeTrue();
        pageModel.ContinueCommand.CanExecute(null).Should().BeTrue();
        pageModel.SelectedCountText.Should().Be("2 selected");
        pageModel.Groups[0].IsSelected.Should().BeTrue();
        pageModel.Groups[1].IsSelected.Should().BeFalse();
        pageModel.Groups[2].IsSelected.Should().BeTrue();

        pageModel.SelectedGroups.Remove(pageModel.Groups[0]);

        pageModel.Groups[0].IsSelected.Should().BeFalse();
        pageModel.SelectedCountText.Should().Be("1 selected");
    }

    [Fact]
    public async Task Appearing_ExcludesApprovedAndPendingGroups_AndListsPendingSeparately()
    {
        var pending = Catalog(MorningId, "Morning Pick Up Soccer", 67, GroupMembershipStatuses.Pending);
        var approved = Catalog(SaturdayId, "Saturday Soccer", 58, GroupMembershipStatuses.Approved);
        var pageModel = CreatePageModel(GroupsReturning(BayArea, pending, approved));

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.Groups.Select(group => group.Id).Should().Equal(BayAreaId);
        pageModel.AlreadyPending.Select(group => group.GroupChatId).Should().Equal(MorningId);
        pageModel.HasAlreadyPending.Should().BeTrue();
    }

    [Fact]
    public async Task Appearing_WhenNoGroups_ShowsEmptyState()
    {
        var pageModel = CreatePageModel(GroupsReturning());

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.StateTitle.Should().Be(LinkGroupPageModel.EmptyTitle);
    }

    [Fact]
    public async Task Continue_SendsEveryselectedId_AndShowsWhichWereApprovedAndWhichWait()
    {
        var groups = GroupsReturning(BayArea, Morning, Saturday);
        IReadOnlyList<Guid>? sentIds = null;
        groups.Setup(x => x.RequestMembershipsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<Guid>, CancellationToken>((ids, _) => sentIds = ids)
            .ReturnsAsync(new MyGroupMembershipsResponse(false, true,
            [
                Membership(BayAreaId, "Bay Area Soccer", GroupMembershipStatuses.Approved),
                Membership(MorningId, "Morning Pick Up Soccer", GroupMembershipStatuses.Pending),
            ]));
        var navigator = Navigator();
        var pageModel = CreatePageModel(groups, navigator);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.SelectedGroups.Add(pageModel.Groups[0]);
        pageModel.SelectedGroups.Add(pageModel.Groups[1]);

        await pageModel.ContinueCommand.ExecuteAsync(null);

        sentIds.Should().BeEquivalentTo([BayAreaId, MorningId]);
        pageModel.HasResult.Should().BeTrue();
        pageModel.IsChoosing.Should().BeFalse();
        pageModel.ResultTitle.Should().Be(LinkGroupPageModel.SomeApprovedTitle);
        pageModel.ResultSubtitle.Should().Be("1 joined · 1 awaiting approval");
        pageModel.Outcomes.Should().SatisfyRespectively(
            bayArea => { bayArea.Name.Should().Be("Bay Area Soccer"); bayArea.IsApproved.Should().BeTrue(); bayArea.StatusVariant.Should().Be(BadgeVariant.Success); },
            morning => { morning.Name.Should().Be("Morning Pick Up Soccer"); morning.IsApproved.Should().BeFalse(); morning.StatusVariant.Should().Be(BadgeVariant.Warning); });
        navigator.Verify(x => x.GoToAuthenticatedAppAsync(), Times.Never, "the outcome is shown before leaving the step");
    }

    [Fact]
    public async Task Continue_WhenEveryRequestIsPending_ShowsWaitingStateAndStillContinuesToApp()
    {
        var groups = GroupsReturning(Morning);
        groups.Setup(x => x.RequestMembershipsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupMembershipsResponse(false, false,
                [Membership(MorningId, "Morning Pick Up Soccer", GroupMembershipStatuses.Pending)]));
        var navigator = Navigator();
        var pageModel = CreatePageModel(groups, navigator);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.SelectedGroups.Add(pageModel.Groups[0]);

        await pageModel.ContinueCommand.ExecuteAsync(null);

        pageModel.ResultTitle.Should().Be(LinkGroupPageModel.AllPendingTitle);
        pageModel.ResultSubtitle.Should().Be("1 request awaiting a group admin.");
        pageModel.HasApprovedGroup.Should().BeFalse();

        await pageModel.ContinueToAppCommand.ExecuteAsync(null);

        navigator.Verify(x => x.GoToAuthenticatedAppAsync(), Times.Once);
    }

    [Fact]
    public async Task Continue_WhenResponseOmitsARequestedGroup_ShowsItAsPending()
    {
        var groups = GroupsReturning(BayArea, Morning);
        groups.Setup(x => x.RequestMembershipsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupMembershipsResponse(false, true,
            [
                Membership(BayAreaId, "Bay Area Soccer", GroupMembershipStatuses.Declined),
                Membership(BayAreaId, "Bay Area Soccer", GroupMembershipStatuses.Approved),
            ]));
        var pageModel = CreatePageModel(groups);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.SelectedGroups.Add(pageModel.Groups[0]);
        pageModel.SelectedGroups.Add(pageModel.Groups[1]);

        await pageModel.ContinueCommand.ExecuteAsync(null);

        // A duplicate row takes the latest status and an omitted group defaults to Pending.
        pageModel.Outcomes.Select(outcome => outcome.Status)
            .Should().Equal(GroupMembershipStatuses.Approved, GroupMembershipStatuses.Pending);
    }

    [Fact]
    public async Task Continue_WhenRequestFails_ShowsAlertAndStaysOnSelection()
    {
        var groups = GroupsReturning(BayArea);
        groups.Setup(x => x.RequestMembershipsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var navigator = Navigator();
        var dialogs = Dialogs();
        var pageModel = CreatePageModel(groups, navigator, dialogs);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.SelectedGroups.Add(pageModel.Groups[0]);

        await pageModel.ContinueCommand.ExecuteAsync(null);

        dialogs.Verify(
            x => x.ShowAlertAsync(LinkGroupPageModel.LinkFailedTitle, It.IsAny<string>(), "OK", It.IsAny<CancellationToken>()),
            Times.Once);
        navigator.Verify(x => x.GoToAuthenticatedAppAsync(), Times.Never);
        pageModel.HasResult.Should().BeFalse();
        pageModel.IsBusy.Should().BeFalse();
        pageModel.CanContinue.Should().BeTrue("the selection survives so the player can retry");
    }

    private static LinkGroupPageModel CreatePageModel(
        Mock<IGroupsClient> groups,
        Mock<IGroupLinkNavigator>? navigator = null,
        Mock<IUserDialogService>? dialogs = null) =>
        new(groups.Object, (navigator ?? Navigator()).Object, (dialogs ?? Dialogs()).Object, new ClientResponseCache(TimeProvider.System));

    private static Mock<IGroupsClient> GroupsReturning(params GroupWithMembershipDto[] catalog)
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(x => x.GetCatalogAsync(It.IsAny<CancellationToken>())).ReturnsAsync(catalog);
        return groups;
    }

    private static GroupWithMembershipDto Catalog(Guid id, string name, int members, string status) =>
        new(id, name, members, status, GroupMemberRoles.Member, PendingRequestCount: 0);

    private static GroupMembershipDto Membership(Guid id, string name, string status) =>
        new(id, name, status, GroupMemberRoles.Member, GroupMembershipSources.Request, new DateTime(2026, 6, 3, 16, 0, 0, DateTimeKind.Utc), null);

    private static Mock<IGroupLinkNavigator> Navigator()
    {
        var navigator = new Mock<IGroupLinkNavigator>();
        navigator.Setup(x => x.GoToAuthenticatedAppAsync()).Returns(Task.CompletedTask);
        return navigator;
    }

    private static Mock<IUserDialogService> Dialogs()
    {
        var dialogs = new Mock<IUserDialogService>();
        dialogs.Setup(x => x.ShowAlertAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return dialogs;
    }
}
