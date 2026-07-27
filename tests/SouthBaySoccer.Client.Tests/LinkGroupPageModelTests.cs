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
    private static readonly GroupChatDto BayArea = new(
        Guid.NewGuid(), "15166436091-1605317459@g.us", "Bay Area Soccer", 349, IsLinked: false, IsPrimary: false);

    [Fact]
    public async Task Appearing_WithGroups_ShowsContentAndBlocksContinueUntilSelected()
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(x => x.GetAvailableGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([BayArea]);
        var pageModel = new LinkGroupPageModel(groups.Object, Navigator().Object, Dialogs().Object, new ClientResponseCache(TimeProvider.System));

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Groups.Should().ContainSingle();
        pageModel.CanContinue.Should().BeFalse("no group is selected yet");

        pageModel.SelectedGroup = BayArea;
        pageModel.CanContinue.Should().BeTrue("a group is now selected");
    }

    [Fact]
    public async Task Appearing_WhenNoGroups_ShowsEmptyState()
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(x => x.GetAvailableGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var pageModel = new LinkGroupPageModel(groups.Object, Navigator().Object, Dialogs().Object, new ClientResponseCache(TimeProvider.System));

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.StateTitle.Should().Be(LinkGroupPageModel.EmptyTitle);
    }

    [Fact]
    public async Task Continue_WhenLinkSucceeds_LinksAndNavigatesToApp()
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(x => x.GetAvailableGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([BayArea]);
        groups.Setup(x => x.LinkAsync(BayArea.ExternalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupsResponse(IsLinked: true, [BayArea]));
        var navigator = Navigator();
        var pageModel = new LinkGroupPageModel(groups.Object, navigator.Object, Dialogs().Object, new ClientResponseCache(TimeProvider.System));

        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.SelectedGroup = BayArea;
        await pageModel.ContinueCommand.ExecuteAsync(null);

        groups.Verify(x => x.LinkAsync(BayArea.ExternalId, It.IsAny<CancellationToken>()), Times.Once);
        navigator.Verify(x => x.GoToAuthenticatedAppAsync(), Times.Once);
    }

    [Fact]
    public async Task Continue_WhenLinkFails_ShowsAlertAndDoesNotNavigate()
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(x => x.GetAvailableGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([BayArea]);
        groups.Setup(x => x.LinkAsync(BayArea.ExternalId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var navigator = Navigator();
        var dialogs = Dialogs();
        var pageModel = new LinkGroupPageModel(groups.Object, navigator.Object, dialogs.Object, new ClientResponseCache(TimeProvider.System));

        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.SelectedGroup = BayArea;
        await pageModel.ContinueCommand.ExecuteAsync(null);

        dialogs.Verify(
            x => x.ShowAlertAsync(LinkGroupPageModel.LinkFailedTitle, It.IsAny<string>(), "OK", It.IsAny<CancellationToken>()),
            Times.Once);
        navigator.Verify(x => x.GoToAuthenticatedAppAsync(), Times.Never);
        pageModel.IsBusy.Should().BeFalse();
    }

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
