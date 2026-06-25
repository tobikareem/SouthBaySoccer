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
            Navigator().Object,
            new GameDayOptions { VenueLocalNow = new DateTime(2026, 6, 20, 19, 35, 0) });

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.StatusLabel.Should().Be("Open");
        pageModel.CanCheckIn.Should().BeTrue();
        pageModel.PrimaryActionText.Should().Be("Check in at field");
    }

    [Fact]
    public async Task CheckIn_OpenWindow_RecordsAttendanceWithoutChangingGoingCount()
    {
        var state = new SeedGameDayState();
        var pageModel = new GameDayPageModel(
            new SeedGameDayClient(state),
            Navigator().Object,
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
    public async Task TeamDraft_AssignedElsewhereRow_IsUnavailable()
    {
        var pageModel = new TeamDraftPageModel(new SeedGameDayClient(new SeedGameDayState()), Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.Players.Should().Contain(item =>
            item.Detail.Contains("Already picked", StringComparison.Ordinal)
            && !item.CanPick);
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
        state.ApproveStat(state.GetPostGameApproval(SeedFixtures.MarinaSessionId).PendingApprovals[0].SubmissionId);
        state.ApproveStat(state.GetPostGameApproval(SeedFixtures.MarinaSessionId).PendingApprovals[1].SubmissionId);

        state.Publish().IsSuccess.Should().BeFalse("the disputed stat still needs review");
        var form = state.RecentFormFor(team.PlayerIds[0], [SouthBaySoccer.Contracts.Profiles.MatchResult.Loss]);

        form.Should().Equal(SouthBaySoccer.Contracts.Profiles.MatchResult.Loss);
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

