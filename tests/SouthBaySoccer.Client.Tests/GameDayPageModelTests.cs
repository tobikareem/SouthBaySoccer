using System.Xml.Linq;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public class GameDayPageModelTests
{
    [Fact]
    public async Task Appearing_DuringWindow_LoadsOpenCheckInState()
    {
        var pageModel = new GameDayPageModel(
            new SeedGameDayClient(new SeedGameDayState()),
            Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.StatusLabel.Should().Be("Open");
        pageModel.CanCheckIn.Should().BeTrue();
        pageModel.PrimaryActionText.Should().Be("Check in at field");
        pageModel.CanDraftTeam.Should().BeTrue();
        pageModel.CanApprovePostGame.Should().BeTrue();
        pageModel.HasBlockReason.Should().BeFalse();
    }

    [Fact]
    public async Task GameDayActions_OpenDraftAndPostGameRoutes()
    {
        var navigator = Navigator();
        var pageModel = new GameDayPageModel(
            new SeedGameDayClient(new SeedGameDayState()),
            navigator.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.OpenTeamDraftCommand.ExecuteAsync(null);
        await pageModel.OpenPostGameApprovalCommand.ExecuteAsync(null);

        navigator.Verify(service => service.OpenTeamDraftAsync(SeedFixtures.MarinaSessionId), Times.Once);
        navigator.Verify(service => service.OpenPostGameApprovalAsync(SeedFixtures.MarinaSessionId), Times.Once);
    }

    [Fact]
    public async Task PostGameActions_WhenPlayerCanSubmitOwnStats_OpenMatchStatsAndRateRoutesForTheMatch()
    {
        var navigator = Navigator();
        var pageModel = new GameDayPageModel(
            new SeedGameDayClient(new SeedGameDayState()),
            navigator.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.OpenMatchStatsCommand.ExecuteAsync(null);
        await pageModel.OpenRateTeammatesCommand.ExecuteAsync(null);

        pageModel.CanSubmitOwnStats.Should().BeTrue();
        navigator.Verify(service => service.OpenMatchStatsAsync(SeedFixtures.FeaturedMatchId), Times.Once);
        navigator.Verify(service => service.OpenRateTeammatesAsync(SeedFixtures.FeaturedMatchId), Times.Once);
    }

    [Fact]
    public async Task PostGameActions_WhenServerWithholdsOwnStats_DoesNotNavigate()
    {
        var context = new SeedGameDayState().GetContext() with { CanSubmitOwnStats = false };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTodayContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        var navigator = Navigator();
        var pageModel = new GameDayPageModel(client.Object, navigator.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.OpenMatchStatsCommand.ExecuteAsync(null);
        await pageModel.OpenRateTeammatesCommand.ExecuteAsync(null);

        pageModel.CanSubmitOwnStats.Should().BeFalse();
        navigator.Verify(service => service.OpenMatchStatsAsync(It.IsAny<Guid>()), Times.Never);
        navigator.Verify(service => service.OpenRateTeammatesAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Appearing_ServerDeniesGameDayActions_DoesNotExposeThem()
    {
        var context = new SeedGameDayState().GetContext() with
        {
            CanAssignCaptains = false,
            CanDraftTeam = false,
            CanApprovePostGame = false,
            CanSubmitOwnStats = false
        };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTodayContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        var pageModel = new GameDayPageModel(
            client.Object,
            Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.HasGameDayActions.Should().BeFalse();
        pageModel.CanAssignCaptains.Should().BeFalse();
        pageModel.CanDraftTeam.Should().BeFalse();
        pageModel.CanApprovePostGame.Should().BeFalse();
        pageModel.OpenCaptainAssignmentCommand.CanExecute(null).Should().BeFalse();
        pageModel.OpenTeamDraftCommand.CanExecute(null).Should().BeFalse();
        pageModel.OpenPostGameApprovalCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Appearing_PlayerProfile_DoesNotForceUnavailableGameDayActions()
    {
        var context = new SeedGameDayState().GetContext() with
        {
            CanAssignCaptains = false,
            CanDraftTeam = false,
            CanApprovePostGame = false,
            CanSubmitOwnStats = false
        };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTodayContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        var navigator = Navigator();
        var pageModel = new GameDayPageModel(
            client.Object,
            navigator.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.OpenCaptainAssignmentCommand.ExecuteAsync(null);
        await pageModel.OpenTeamDraftCommand.ExecuteAsync(null);
        await pageModel.OpenPostGameApprovalCommand.ExecuteAsync(null);

        pageModel.HasGameDayActions.Should().BeFalse();
        pageModel.OpenCaptainAssignmentCommand.CanExecute(null).Should().BeFalse();
        pageModel.OpenTeamDraftCommand.CanExecute(null).Should().BeFalse();
        pageModel.OpenPostGameApprovalCommand.CanExecute(null).Should().BeFalse();
        navigator.Verify(service => service.OpenCaptainAssignmentAsync(It.IsAny<Guid>()), Times.Never);
        navigator.Verify(service => service.OpenTeamDraftAsync(It.IsAny<Guid>()), Times.Never);
        navigator.Verify(service => service.OpenPostGameApprovalAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CheckIn_OpenWindow_RecordsAttendanceWithoutChangingGoingCount()
    {
        var state = new SeedGameDayState();
        var pageModel = new GameDayPageModel(
            new SeedGameDayClient(state),
            Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        var goingCount = pageModel.GoingCount;
        await pageModel.CheckInCommand.ExecuteAsync(null);

        pageModel.StatusLabel.Should().Be("Checked in");
        pageModel.CheckedInCount.Should().Be(11);
        pageModel.GoingCount.Should().Be(goingCount);
    }

    [Fact]
    public async Task CheckIn_WhenNetworkUnavailable_ShowsOfflineState()
    {
        var context = new SeedGameDayState().GetContext();
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTodayContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        client
            .Setup(service => service.CheckInAsync(
                context.SessionId,
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.CheckInCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Offline);
    }

    [Fact]
    public void SeedLateCheckIn_RecordsSelectedPlayerAndRemovesThemFromOverrideList()
    {
        var state = new SeedGameDayState();
        var before = state.GetClosedContext();
        var player = before.LateCheckInPlayers!.First();

        var result = state.LateCheckIn(
            before.SessionId,
            player.PlayerProfileId,
            "Traffic delay",
            Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        var after = state.GetClosedContext();
        after.LateCount.Should().Be(1);
        after.LateCheckInPlayers.Should().NotContain(item => item.PlayerProfileId == player.PlayerProfileId);
    }

    [Fact]
    public async Task Appearing_ServerReturnsClosedWindow_DisablesSelfCheckIn()
    {
        var context = new SeedGameDayState().GetContext() with
        {
            Status = GameDayStatus.Closed,
            StatusLabel = "Closed",
            IsSelfCheckInAvailable = false,
            PrimaryActionText = "GameAdmin override required",
            BlockReason = "Check-in is closed. Ask a GameAdmin to record a late arrival."
        };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTodayContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        var pageModel = new GameDayPageModel(
            client.Object,
            Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.StatusLabel.Should().Be("Closed");
        pageModel.CanCheckIn.Should().BeFalse();
        pageModel.BlockReason.Should().Contain("GameAdmin");
    }

    [Fact]
    public async Task CaptainAssignment_ToggleSelection_StopsAtCaptainCount()
    {
        var pageModel = new CaptainAssignmentPageModel(new SeedGameDayClient(new SeedGameDayState()), Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.SelectCaptainCountCommand.Execute(2);
        foreach (var item in pageModel.Players)
        {
            item.IsSelected = false;
        }

        pageModel.ToggleCaptainCommand.Execute(pageModel.Players[0]);
        pageModel.ToggleCaptainCommand.Execute(pageModel.Players[1]);
        pageModel.ToggleCaptainCommand.Execute(pageModel.Players[2]);

        pageModel.Players.Count(item => item.IsSelected).Should().Be(2);
        pageModel.Players.Should().OnlyContain(item => item.Detail.StartsWith("checked in", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CaptainAssignment_SelectCaptainCount_WhenXamlPassesString_UpdatesCount()
    {
        var pageModel = new CaptainAssignmentPageModel(new SeedGameDayClient(new SeedGameDayState()), Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.SelectCaptainCountCommand.Execute("4");

        pageModel.CaptainCount.Should().Be(4);
        pageModel.SelectedCountText.Should().Contain("max 4");
    }

    [Fact]
    public async Task TeamDraft_AssignedElsewhereRow_IsUnavailable()
    {
        var pageModel = new TeamDraftPageModel(new SeedGameDayClient(new SeedGameDayState()), Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.Players.Should().Contain(item =>
            item.Detail.Contains("Already picked", StringComparison.Ordinal)
            && !item.CanPick);
    }

    [Fact]
    public async Task TeamDraft_SaveTeamPicks_PersistsSessionScopedAssignment()
    {
        var state = new SeedGameDayState();
        var pageModel = new TeamDraftPageModel(new SeedGameDayClient(state), Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        var openPlayer = pageModel.Players.First(item => item.CanPick && !item.IsSelected);
        pageModel.TogglePickCommand.Execute(openPlayer);
        await pageModel.SaveCommand.ExecuteAsync(null);

        var draft = state.GetTeamDraft(SeedFixtures.MarinaSessionId);
        var team = draft.Teams.First(item => item.TeamId == draft.TeamId);
        team.PlayerIds.Should().Contain(openPlayer.PlayerId);
    }

    [Fact]
    public async Task Appearing_SeedContext_PopulatesGoingWaitlistRosterAndAdminCheckIn()
    {
        var pageModel = new GameDayPageModel(
            new SeedGameDayClient(new SeedGameDayState()),
            Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.CanManageCheckIns.Should().BeTrue();
        pageModel.HasRoster.Should().BeTrue();
        pageModel.Roster.Should().Contain(item => item.IsWaitlist);
        pageModel.Roster.Should().Contain(item => item.CanCheckIn);
    }

    [Fact]
    public async Task AdminCheckIn_ForConfirmedPlayer_ChecksInAndClearsRowAction()
    {
        var state = new SeedGameDayState();
        var pageModel = new GameDayPageModel(new SeedGameDayClient(state), Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        var target = pageModel.Roster.First(item => item.CanCheckIn);
        var before = pageModel.CheckedInCount;

        await pageModel.AdminCheckInCommand.ExecuteAsync(target);

        pageModel.CheckedInCount.Should().Be(before + 1);
        var updated = pageModel.Roster.Single(item => item.PlayerProfileId == target.PlayerProfileId);
        updated.IsCheckedIn.Should().BeTrue();
        updated.CanCheckIn.Should().BeFalse();
    }

    [Fact]
    public async Task TeamDraft_WhenAdminCanManageAllTeams_SwitchingTeamReprojectsRoster()
    {
        var teamOne = Guid.NewGuid();
        var teamTwo = Guid.NewGuid();
        var captainOne = Guid.NewGuid();
        var captainTwo = Guid.NewGuid();
        var draft = new TeamDraftDto(
            SeedFixtures.MarinaSessionId,
            Guid.NewGuid(),
            teamOne,
            "Team Green",
            "Cap One",
            CanPickPlayers: true,
            IsLocked: false,
            TeamCount: 2,
            CheckedInPlayers: [],
            Teams:
            [
                new MatchTeamDto(teamOne, "Team Green", captainOne, "Cap One", [captainOne]),
                new MatchTeamDto(teamTwo, "Team White", captainTwo, "Cap Two", [captainTwo])
            ],
            CanManageAllTeams: true);
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTeamDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        var pageModel = new TeamDraftPageModel(client.Object, Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.CanManageAllTeams.Should().BeTrue();
        pageModel.Teams.Should().HaveCount(2);
        pageModel.TeamName.Should().Be("Team Green");

        pageModel.SelectTeamCommand.Execute(teamTwo);

        pageModel.SelectedTeamId.Should().Be(teamTwo);
        pageModel.TeamName.Should().Be("Team White");
        pageModel.CaptainName.Should().Be("Cap Two");
    }

    [Fact]
    public void TeamResultItem_WithThreeTeams_RecordsAsManyGamesAsTheRotationActuallyPlayed()
    {
        var item = new TeamResultItem(Guid.NewGuid(), "Team Green", 3, 0, 0, 0);

        // A winner-stays-on rotation: five games for this side, more than the two opponents it has.
        item.TryUpdate(3, 1, 1).Should().BeTrue();

        item.GamesRecorded.Should().Be(5);
        item.Detail.Should().Be("5 games recorded");
    }

    [Fact]
    public void TeamResultItem_NegativeCounters_AreRejected()
    {
        var item = new TeamResultItem(Guid.NewGuid(), "Team Green", 2, 1, 0, 0);

        item.TryUpdate(-1, 0, 0).Should().BeFalse();

        item.Wins.Should().Be(1);
    }

    [Fact]
    public void SeedGameDayState_Publish_DerivesRecentFormForAssignedPlayersOnly()
    {
        var state = new SeedGameDayState();
        var draft = state.GetTeamDraft(SeedFixtures.MarinaSessionId);
        var team = draft.Teams.First();
        state.SaveTeamResult(new TeamResultUpdateDto(team.TeamId, 1, 0, 0)).Should().Be(ClientCommandResult.Success);
        foreach (var approval in state.GetPostGameApproval(SeedFixtures.MarinaSessionId).PendingApprovals)
        {
            state.ApproveStat(approval.SubmissionId).Should().Be(ClientCommandResult.Success);
        }

        state.Publish().Should().Be(ClientCommandResult.Success);
        var assignedForm = state.RecentFormFor(team.PlayerIds[0], [SouthBaySoccer.Contracts.Profiles.MatchResult.Loss]);
        var unassigned = draft.CheckedInPlayers.First(player => draft.Teams.All(matchTeam => !matchTeam.PlayerIds.Contains(player.Player.Id)));
        var unassignedForm = state.RecentFormFor(unassigned.Player.Id, [SouthBaySoccer.Contracts.Profiles.MatchResult.Loss]);

        assignedForm.First().Should().Be(SouthBaySoccer.Contracts.Profiles.MatchResult.Win);
        unassignedForm.Should().Equal(SouthBaySoccer.Contracts.Profiles.MatchResult.Loss);
    }

    [Fact]
    public void GameDayXaml_UsesSharedControlsAndFontAwesome()
    {
        var gameDay = LoadXaml("GameDayPage.xaml").ToString();
        var captains = LoadXaml("CaptainAssignmentPage.xaml").ToString();
        var draft = LoadXaml("TeamDraftPage.xaml").ToString();
        var postgame = LoadXaml("PostGameApprovalPage.xaml").ToString();

        gameDay.Should().Contain("StateView").And.Contain("BrandCard").And.Contain("StatTile");
        gameDay.Should().Contain("AdminCheckInCommand").And.Contain("{Binding Roster}");
        captains.Should().Contain("PlayerRow").And.Contain("CheckBox");
        draft.Should().Contain("TeamDraft.PickPlayer").And.Contain("PlayerRow");
        draft.Should().Contain("SelectTeamCommand");
        postgame.Should().Contain("CounterStepper").And.Contain("Publish approved match");
        (gameDay + captains + draft + postgame).Should().Contain("FontAwesomeGlyphs");
    }

    private static Mock<IGameDayNavigator> Navigator()
    {
        var navigator = new Mock<IGameDayNavigator>();
        navigator.Setup(service => service.OpenCaptainAssignmentAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        navigator.Setup(service => service.OpenTeamDraftAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        navigator.Setup(service => service.OpenPostGameApprovalAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        navigator.Setup(service => service.GoBackAsync()).Returns(Task.CompletedTask);
        return navigator;
    }

    private static XDocument LoadXaml(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Xaml", fileName);
        File.Exists(path).Should().BeTrue($"the test project must copy {fileName} to its output");
        return XDocument.Load(path);
    }
}
