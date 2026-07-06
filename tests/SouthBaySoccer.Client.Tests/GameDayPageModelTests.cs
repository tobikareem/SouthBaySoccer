using System.Xml.Linq;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Contracts.Profiles;
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
            Navigator().Object,
            ProfileClientReturning().Object,
            new GameDayOptions { VenueLocalNow = new DateTime(2026, 6, 20, 19, 35, 0) });

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
            navigator.Object,
            ProfileClientReturning().Object,
            new GameDayOptions { VenueLocalNow = new DateTime(2026, 6, 20, 19, 35, 0) });

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.OpenTeamDraftCommand.ExecuteAsync(null);
        await pageModel.OpenPostGameApprovalCommand.ExecuteAsync(null);

        navigator.Verify(service => service.OpenTeamDraftAsync(SeedFixtures.MarinaSessionId), Times.Once);
        navigator.Verify(service => service.OpenPostGameApprovalAsync(SeedFixtures.MarinaSessionId), Times.Once);
    }

    [Fact]
    public async Task Appearing_AdminProfile_ForcesAllGameDayActionsVisible()
    {
        var context = new SeedGameDayState().GetContext() with
        {
            CanAssignCaptains = false,
            CanDraftTeam = false,
            CanApprovePostGame = false
        };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTodayContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        var pageModel = new GameDayPageModel(
            client.Object,
            Navigator().Object,
            ProfileClientReturning("Admin").Object,
            new GameDayOptions { VenueLocalNow = new DateTime(2026, 6, 20, 19, 35, 0) });

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.IsAdmin.Should().BeTrue();
        pageModel.HasGameDayActions.Should().BeTrue();
        pageModel.CanAssignCaptains.Should().BeTrue();
        pageModel.CanDraftTeam.Should().BeTrue();
        pageModel.CanApprovePostGame.Should().BeTrue();
        pageModel.OpenCaptainAssignmentCommand.CanExecute(null).Should().BeTrue();
        pageModel.OpenTeamDraftCommand.CanExecute(null).Should().BeTrue();
        pageModel.OpenPostGameApprovalCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Appearing_PlayerProfile_DoesNotForceUnavailableGameDayActions()
    {
        var context = new SeedGameDayState().GetContext() with
        {
            CanAssignCaptains = false,
            CanDraftTeam = false,
            CanApprovePostGame = false
        };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTodayContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        var navigator = Navigator();
        var pageModel = new GameDayPageModel(
            client.Object,
            navigator.Object,
            ProfileClientReturning("Player").Object,
            new GameDayOptions { VenueLocalNow = new DateTime(2026, 6, 20, 19, 35, 0) });

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.OpenCaptainAssignmentCommand.ExecuteAsync(null);
        await pageModel.OpenTeamDraftCommand.ExecuteAsync(null);
        await pageModel.OpenPostGameApprovalCommand.ExecuteAsync(null);

        pageModel.IsAdmin.Should().BeFalse();
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
            Navigator().Object,
            ProfileClientReturning().Object,
            new GameDayOptions { VenueLocalNow = new DateTime(2026, 6, 20, 19, 35, 0) });

        await pageModel.AppearingCommand.ExecuteAsync(null);
        var goingCount = pageModel.GoingCount;
        await pageModel.CheckInCommand.ExecuteAsync(null);

        pageModel.StatusLabel.Should().Be("Checked in");
        pageModel.CheckedInCount.Should().Be(11);
        pageModel.GoingCount.Should().Be(goingCount);
    }

    [Fact]
    public async Task Appearing_AfterWindow_DisablesSelfCheckIn()
    {
        var pageModel = new GameDayPageModel(
            new SeedGameDayClient(new SeedGameDayState()),
            Navigator().Object,
            ProfileClientReturning().Object,
            new GameDayOptions { VenueLocalNow = new DateTime(2026, 6, 20, 19, 50, 0) });

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.StatusLabel.Should().Be("Closed");
        pageModel.CanCheckIn.Should().BeFalse();
        pageModel.BlockReason.Should().Contain("GameAdmin override");
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
    public void TeamResultItem_Totals_CannotExceedTeamCountMinusOne()
    {
        var item = new TeamResultItem(Guid.NewGuid(), "Team Green", 3, 0, 0, 0);

        item.Wins = 1;
        item.Draws = 1;
        item.Losses = 1;

        (item.Wins + item.Draws + item.Losses).Should().Be(2);
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
        captains.Should().Contain("PlayerRow").And.Contain("CheckBox");
        draft.Should().Contain("TeamDraft.PickPlayer").And.Contain("PlayerRow");
        postgame.Should().Contain("CounterStepper").And.Contain("Publish approved match");
        (gameDay + captains + draft + postgame).Should().Contain("FontAwesomeGlyphs");
    }

    private static Mock<IProfileClient> ProfileClientReturning(string role = "GameAdmin")
    {
        var profileClient = new Mock<IProfileClient>();
        profileClient
            .Setup(client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerProfileDto(
                SeedFixtures.CurrentPlayerId,
                "Tobi Kareem",
                "Captain",
                "TK",
                new CareerStatsDto(0, 0, 0, 0, 0, 0),
                [],
                null,
                role));

        return profileClient;
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
