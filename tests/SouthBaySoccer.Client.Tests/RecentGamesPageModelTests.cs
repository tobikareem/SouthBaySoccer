using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Clients;
using Xunit;

namespace SouthBaySoccer.Client.Tests;

public sealed class RecentGamesPageModelTests
{
    private static readonly Guid SessionId = Guid.Parse("20000000-0000-0000-0000-000000000009");
    private static readonly Guid MatchId = Guid.Parse("30000000-0000-0000-0000-000000000009");

    [Fact]
    public async Task Appearing_WhenRecentGamesExist_ListsThemWithStatusAndPendingCount()
    {
        var pageModel = CreatePageModel(out _, Game(pendingApprovals: 2, status: "Completed"));

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Games.Should().HaveCount(1);
        pageModel.Games[0].Title.Should().Be("Marina Field - Wednesday pickup");
        pageModel.Games[0].StatusLabel.Should().Be("Results recorded");
        pageModel.Games[0].HasPending.Should().BeTrue();
        pageModel.Games[0].PendingLabel.Should().Be("2 stats awaiting review");
    }

    [Fact]
    public async Task Appearing_WhenNothingIsEditable_ShowsEmptyState()
    {
        var pageModel = CreatePageModel(out _);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.StateTitle.Should().Be(RecentGamesPageModel.EmptyTitle);
        pageModel.Games.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectingAGame_OpensCaptainsTeamsAndStatsForThatSession()
    {
        var pageModel = CreatePageModel(out var navigator, Game(pendingApprovals: 1, status: "InProgress"));
        await pageModel.AppearingCommand.ExecuteAsync(null);
        var game = pageModel.Games[0];

        await pageModel.OpenCaptainsCommand.ExecuteAsync(game);
        await pageModel.OpenTeamsCommand.ExecuteAsync(game);
        await pageModel.OpenStatsCommand.ExecuteAsync(game);

        navigator.Verify(x => x.OpenCaptainAssignmentAsync(SessionId), Times.Once);
        navigator.Verify(x => x.OpenTeamDraftAsync(SessionId), Times.Once);
        navigator.Verify(x => x.OpenPostGameApprovalAsync(SessionId), Times.Once);
    }

    [Fact]
    public async Task Appearing_WhenOffline_ShowsOfflineState()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(x => x.GetRecentGamesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException());
        var pageModel = new RecentGamesPageModel(client.Object, new Mock<IGameDayNavigator>().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Offline);
    }

    private static RecentGameDto Game(int pendingApprovals, string status) =>
        new(
            SessionId,
            MatchId,
            "Marina Field - Wednesday pickup",
            "Marina Field",
            "Wed Jul 22, 7:30 PM",
            status,
            2,
            pendingApprovals,
            CanEditTeams: true);

    private static RecentGamesPageModel CreatePageModel(
        out Mock<IGameDayNavigator> navigator,
        params RecentGameDto[] games)
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(x => x.GetRecentGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(games);
        navigator = new Mock<IGameDayNavigator>();
        navigator.Setup(x => x.OpenCaptainAssignmentAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        navigator.Setup(x => x.OpenTeamDraftAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        navigator.Setup(x => x.OpenPostGameApprovalAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        return new RecentGamesPageModel(client.Object, navigator.Object);
    }
}
