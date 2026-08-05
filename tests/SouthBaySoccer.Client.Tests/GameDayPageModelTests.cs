using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Client.Tests.TestSupport;

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
    public async Task Appearing_WhenMultipleGamesToday_ShowsPickerAndSwitchesGame()
    {
        var baseContext = new SeedGameDayState().GetContext();
        var otherGameId = Guid.NewGuid();
        var startsAt = new DateTime(2026, 7, 24, 2, 30, 0, DateTimeKind.Utc);
        var context = baseContext with
        {
            TodaysGames =
            [
                new GameDayGameOptionDto(baseContext.SessionId, "Bay Area", "Marina", startsAt, "Going", true),
                new GameDayGameOptionDto(otherGameId, "Fire FC", "Stanford", startsAt.AddHours(1), "Going", false),
            ],
        };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.HasMultipleGames.Should().BeTrue();
        pageModel.TodaysGames.Should().HaveCount(2);

        await pageModel.SelectGameCommand.ExecuteAsync(otherGameId);

        // Switching loads the chosen game explicitly (first load used the server's auto-pick, null).
        client.Verify(service => service.GetTodayContextAsync(otherGameId, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(service => service.GetTodayContextAsync(null, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecentGames_WhenPlayerHasTodayGame_PreservesTodayAndLetsPlayerSelectHistory()
    {
        var fireFc = LastGameWith() with { Title = "Fire FC Thursday pickup" };
        var bayArea = LastGameWith() with { Title = "Bay Area Soccer Game" };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeedGameDayState().GetContext() with { CanShowAllGames = false });
        client
            .Setup(service => service.GetRecentGameSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([fireFc, bayArea]);
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.ShowPlayerViewSwitch.Should().BeTrue();
        pageModel.IsShowingRecentGames.Should().BeFalse();
        pageModel.IsParticipant.Should().BeTrue();

        pageModel.SelectPlayerViewCommand.Execute(GameDayPageModel.RecentGamesViewLabel);
        pageModel.SelectRecentGameCommand.Execute(new RecentGameSummaryItem(bayArea));

        pageModel.IsShowingRecentGames.Should().BeTrue();
        pageModel.IsParticipant.Should().BeFalse();
        pageModel.LastGame.Should().BeSameAs(bayArea);

        pageModel.SelectPlayerViewCommand.Execute(GameDayPageModel.TodayViewLabel);

        pageModel.IsShowingRecentGames.Should().BeFalse();
        pageModel.IsParticipant.Should().BeTrue();
        pageModel.LastGame.Should().BeNull();
    }

    [Fact]
    public async Task Appearing_WhenRecentHistoryFails_LogsSanitizedFailureAndKeepsTodayAvailable()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeedGameDayState().GetContext() with { CanShowAllGames = false });
        client
            .Setup(service => service.GetRecentGameSummariesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sensitive response content"));
        var logger = new Mock<ILogger<GameDayPageModel>>();
        var pageModel = new GameDayPageModel(
            client.Object,
            Navigator().Object,
            logger: logger.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.IsParticipant.Should().BeTrue();
        pageModel.RecentGameSummaries.Should().BeEmpty();
        logger.Verify(
            candidate => candidate.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("InvalidOperationException")
                    && !state.ToString()!.Contains("sensitive response content")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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
            .Setup(service => service.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
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
            .Setup(service => service.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
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
            .Setup(service => service.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
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
        // The checked-in tile count is derived from the roster, so it matches the popup list on tap.
        pageModel.CheckedInCount.Should().Be(pageModel.Roster.Count(item => item.IsCheckedIn));
        pageModel.GoingCount.Should().Be(goingCount);
    }

    [Fact]
    public async Task CheckIn_WhenNetworkUnavailable_ShowsOfflineState()
    {
        var context = new SeedGameDayState().GetContext();
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
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
            .Setup(service => service.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
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
    public async Task CaptainAssignment_WhenSelectionMatchesGranted_DisablesGrantUntilChanged()
    {
        var cap1 = Guid.NewGuid();
        var cap2 = Guid.NewGuid();
        var third = Guid.NewGuid();
        var players = new[] { cap1, cap2, third };
        var checkedIn = players
            .Select((id, n) => new CheckedInPlayerDto(
                new PlayerSummaryDto(id, $"Player {n}", $"P{n}", "Midfielder", false), "going"))
            .ToArray();
        var dto = new CaptainAssignmentDto(
            Guid.NewGuid(), Guid.NewGuid(), 2, new[] { 2, 3, 4 },
            new[] { cap1, cap2 }, checkedIn);
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetCaptainAssignmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        var pageModel = new CaptainAssignmentPageModel(client.Object, Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.IsCurrentSelectionGranted.Should().BeTrue();
        pageModel.HasGrantStatus.Should().BeTrue();
        pageModel.GrantCommand.CanExecute(null).Should().BeFalse("the current captains are already granted");

        // Swap one captain: Grant re-enables so the admin can re-cut the teams.
        pageModel.ToggleCaptainCommand.Execute(pageModel.Players.First(p => p.PlayerId == cap1));
        pageModel.ToggleCaptainCommand.Execute(pageModel.Players.First(p => p.PlayerId == third));

        pageModel.IsCurrentSelectionGranted.Should().BeFalse();
        pageModel.GrantCommand.CanExecute(null).Should().BeTrue();
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
    public async Task TeamDraft_PerTeamCap_ShowsShareAndBlocksPicksBeyondIt()
    {
        // 15 eligible players across 3 teams => 5 per team; the captain already fills one slot.
        var pageModel = await LoadDraftAsync(playerCount: 15, teamCount: 3, currentTeamIndex: 0);

        pageModel.TeamCap.Should().Be(5);
        pageModel.SelectedCount.Should().Be(1);
        pageModel.Summary.Should().Be("1 of 5 selected (4 left)");

        foreach (var open in pageModel.Players.Where(item => item.IsPickable && !item.IsSelected).Take(4).ToList())
        {
            pageModel.TogglePickCommand.Execute(open);
        }

        pageModel.SelectedCount.Should().Be(5);
        pageModel.Summary.Should().Be("5 of 5 selected (full)");

        // Once full, the remaining open rows are greyed and a further pick is refused.
        var blocked = pageModel.Players.First(item => item.CanPick && !item.IsSelected);
        blocked.IsPickable.Should().BeFalse();
        blocked.IsDimmed.Should().BeTrue();
        pageModel.TogglePickCommand.Execute(blocked);
        pageModel.SelectedCount.Should().Be(5);
    }

    [Fact]
    public async Task TeamDraft_UnevenRoster_GivesTheEarlierTeamTheExtraSlot()
    {
        // 16 players across 3 teams => caps of 6, 5, 5.
        var firstTeam = await LoadDraftAsync(playerCount: 16, teamCount: 3, currentTeamIndex: 0);
        firstTeam.TeamCap.Should().Be(6);

        var lastTeam = await LoadDraftAsync(playerCount: 16, teamCount: 3, currentTeamIndex: 2);
        lastTeam.TeamCap.Should().Be(5);
    }

    [Fact]
    public async Task CaptainAssignment_TapOrderBecomesRankAndGrantPayloadOrder()
    {
        var pageModel = new CaptainAssignmentPageModel(new SeedGameDayClient(new SeedGameDayState()), Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.SelectCaptainCountCommand.Execute(2);
        // Deselect everything through the command so the rank list stays in sync.
        foreach (var item in pageModel.Players.Where(item => item.IsSelected).ToArray())
        {
            pageModel.ToggleCaptainCommand.Execute(item);
        }

        // Tap in reverse roster order: the SECOND roster row tapped first becomes the 1st captain.
        var first = pageModel.Players[1];
        var second = pageModel.Players[0];
        pageModel.ToggleCaptainCommand.Execute(first);
        pageModel.ToggleCaptainCommand.Execute(second);

        first.RankLabel.Should().Be("1st captain");
        second.RankLabel.Should().Be("2nd captain");

        // Deselecting the 1st re-ranks the remaining captain up.
        pageModel.ToggleCaptainCommand.Execute(first);
        second.RankLabel.Should().Be("1st captain");
        first.RankLabel.Should().BeEmpty();
    }

    [Fact]
    public async Task TeamDraft_ServerCaps_WinOverLocalSplit()
    {
        // Server says 7/4/4 (policy is server-owned); the local 15/3 split would say 5/5/5.
        var pageModel = await LoadDraftAsync(playerCount: 15, teamCount: 3, currentTeamIndex: 0, teamCaps: [7, 4, 4]);

        pageModel.TeamCap.Should().Be(7);
    }

    [Fact]
    public async Task TeamDraft_CaptainOnTheirTurn_PicksStraightToServerAndReloads()
    {
        var (draft, players, _) = BuildDraft(playerCount: 6, teamCount: 2, currentTeamIndex: 0);
        var captainDraft = draft with { CanManageAllTeams = false, IsMyTurn = true, OnTheClockLabel = "Your turn — pick 1 player" };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTeamDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(captainDraft);
        client
            .Setup(service => service.DraftPickAsync(captainDraft.SessionId, It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Success);
        var pageModel = new TeamDraftPageModel(client.Object, Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.HasTurnBanner.Should().BeTrue();
        var target = pageModel.Players.First(item => item.IsPickable && !item.IsSelected);
        await pageModel.TogglePickCommand.ExecuteAsync(target);

        client.Verify(service => service.DraftPickAsync(captainDraft.SessionId, target.PlayerId, It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(service => service.GetTeamDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(2), "a successful pick reloads the draft");
        client.Verify(
            service => service.SaveTeamPicksAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TeamDraft_CaptainOffTurn_HasNoPickableRows()
    {
        var (draft, _, _) = BuildDraft(playerCount: 6, teamCount: 2, currentTeamIndex: 0);
        var offTurn = draft with { CanManageAllTeams = false, IsMyTurn = false, OnTheClockLabel = "On the clock: Team 2" };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTeamDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(offTurn);
        var pageModel = new TeamDraftPageModel(client.Object, Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.OnTheClockLabel.Should().Be("On the clock: Team 2");
        pageModel.Players.Where(item => !item.IsSelected).Should().OnlyContain(item => !item.IsPickable);
    }

    [Fact]
    public async Task TeamDraft_AutoBalance_ConfirmsThenReloads()
    {
        var (draft, _, _) = BuildDraft(playerCount: 6, teamCount: 2, currentTeamIndex: 0);
        var adminDraft = draft with { CanAutoBalance = true };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTeamDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminDraft);
        client
            .Setup(service => service.AutoBalanceTeamsAsync(adminDraft.SessionId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Success);
        var dialogs = new Mock<IUserDialogService>();
        dialogs
            .Setup(d => d.ShowConfirmationAsync(
                TeamDraftPageModel.AutoBalanceConfirmTitle,
                TeamDraftPageModel.AutoBalanceConfirmMessage,
                "Balance", "Cancel", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var pageModel = new TeamDraftPageModel(client.Object, Navigator().Object, dialogs.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.AutoBalanceCommand.ExecuteAsync(null);
        await pageModel.AutoBalanceCommand.ExecuteAsync(null);

        // The server owns the deal number; the client just fires the mutation each time.
        client.Verify(service => service.AutoBalanceTeamsAsync(adminDraft.SessionId, It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        client.Verify(service => service.GetTeamDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task TeamDraft_SwitchingTeamsWithUnsavedPicks_AsksBeforeDiscarding()
    {
        var (draft, players, teamIds) = BuildDraft(playerCount: 6, teamCount: 2, currentTeamIndex: 0);
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTeamDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft with { CanManageAllTeams = true });
        var dialogs = new Mock<IUserDialogService>();
        dialogs
            .Setup(d => d.ShowConfirmationAsync(
                TeamDraftPageModel.DiscardPicksTitle,
                TeamDraftPageModel.DiscardPicksMessage,
                "Switch team", "Stay", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var pageModel = new TeamDraftPageModel(client.Object, Navigator().Object, dialogs.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        // Dirty the current team's selection, then try to switch away.
        var toggle = pageModel.Players.First(item => item.IsPickable && !item.IsSelected);
        await pageModel.TogglePickCommand.ExecuteAsync(toggle);

        await pageModel.SelectTeamCommand.ExecuteAsync(teamIds[1]);

        dialogs.Verify(
            d => d.ShowConfirmationAsync(
                TeamDraftPageModel.DiscardPicksTitle,
                TeamDraftPageModel.DiscardPicksMessage,
                "Switch team", "Stay", It.IsAny<CancellationToken>()),
            Times.Once);
        // Declined: the dirty selection survives and the team did not switch.
        pageModel.Players.Single(item => item.PlayerId == toggle.PlayerId).IsSelected.Should().BeTrue();
        _ = players;
    }

    [Fact]
    public async Task TeamDraft_WhileBusy_SaveAndTogglesAreInert()
    {
        var (draft, _, _) = BuildDraft(playerCount: 6, teamCount: 2, currentTeamIndex: 0);
        var adminDraft = draft with { CanManageAllTeams = true, CanAutoBalance = true };
        var balanceGate = new TaskCompletionSource<ClientCommandResult>();
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTeamDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminDraft);
        client
            .Setup(service => service.AutoBalanceTeamsAsync(adminDraft.SessionId, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(balanceGate.Task);
        var dialogs = new Mock<IUserDialogService>();
        dialogs
            .Setup(d => d.ShowConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var pageModel = new TeamDraftPageModel(client.Object, Navigator().Object, dialogs.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        var balancing = pageModel.AutoBalanceCommand.ExecuteAsync(null);

        // Mid-balance nothing may mutate the (about-to-be-stale) selection or fire a save.
        pageModel.SaveCommand.CanExecute(null).Should().BeFalse();
        var toggle = pageModel.Players.First(item => item.IsPickable && !item.IsSelected);
        await pageModel.TogglePickCommand.ExecuteAsync(toggle);
        pageModel.Players.Single(item => item.PlayerId == toggle.PlayerId).IsSelected.Should().BeFalse();

        balanceGate.SetResult(ClientCommandResult.Success);
        await balancing;
        pageModel.SaveCommand.CanExecute(null).Should().BeTrue("busy state clears once the balance completes");
    }

    [Fact]
    public async Task TeamDraft_AutoBalance_CancelledConfirmDoesNothing()
    {
        var (draft, _, _) = BuildDraft(playerCount: 6, teamCount: 2, currentTeamIndex: 0);
        var adminDraft = draft with { CanAutoBalance = true };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTeamDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminDraft);
        var dialogs = new Mock<IUserDialogService>();
        dialogs
            .Setup(d => d.ShowConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var pageModel = new TeamDraftPageModel(client.Object, Navigator().Object, dialogs.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.AutoBalanceCommand.ExecuteAsync(null);

        client.Verify(
            service => service.AutoBalanceTeamsAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TeamDraftPolling_AfterFailure_BacksOffFromTwoToFiveSeconds()
    {
        var (draft, _, _) = BuildDraft(playerCount: 6, teamCount: 2, currentTeamIndex: 0);
        var client = new Mock<IGameDayClient>();
        client.Setup(service => service.GetTeamDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        client.Setup(service => service.GetTeamDraftIfChangedAsync(
                draft.SessionId,
                draft.DraftRevision,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var delay = new ControlledPollingDelay();
        var pageModel = new TeamDraftPageModel(client.Object, Navigator().Object, pollingDelay: delay);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await delay.WaitForDelayCountAsync(1);
        delay.Delays[0].Should().Be(TimeSpan.FromSeconds(2));
        delay.ReleaseNext();
        await delay.WaitForDelayCountAsync(2);

        delay.Delays[1].Should().Be(TimeSpan.FromSeconds(5));
        await pageModel.DisappearingCommand.ExecuteAsync(null);
    }

    private static (TeamDraftDto Draft, Guid[] Players, Guid[] TeamIds) BuildDraft(
        int playerCount,
        int teamCount,
        int currentTeamIndex,
        IReadOnlyList<int>? teamCaps = null)
    {
        var players = Enumerable.Range(0, playerCount).Select(_ => Guid.NewGuid()).ToArray();
        var teamIds = Enumerable.Range(0, teamCount).Select(_ => Guid.NewGuid()).ToArray();
        // The first `teamCount` players are the captains, one per team (already on their own roster).
        var teams = Enumerable.Range(0, teamCount)
            .Select(i => new MatchTeamDto(teamIds[i], $"Team {i + 1}", players[i], $"Captain {i + 1}", [players[i]]))
            .ToArray();
        var checkedIn = players
            .Select((id, n) => new CheckedInPlayerDto(
                new PlayerSummaryDto(id, $"Player {n}", $"P{n}", "Midfielder", false), "going"))
            .ToArray();
        var draft = new TeamDraftDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            teamIds[currentTeamIndex],
            $"Team {currentTeamIndex + 1}",
            $"Captain {currentTeamIndex + 1}",
            CanPickPlayers: true,
            IsLocked: false,
            TeamCount: teamCount,
            CheckedInPlayers: checkedIn,
            Teams: teams,
            // Multi-select + Save is the admin flow since TEAM-5; captains draft in snake turns.
            CanManageAllTeams: true,
            TeamCaps: teamCaps);
        return (draft, players, teamIds);
    }

    private static async Task<TeamDraftPageModel> LoadDraftAsync(
        int playerCount,
        int teamCount,
        int currentTeamIndex,
        IReadOnlyList<int>? teamCaps = null)
    {
        var (draft, _, _) = BuildDraft(playerCount, teamCount, currentTeamIndex, teamCaps);
        var client = new Mock<IGameDayClient>();
        client
            .Setup(service => service.GetTeamDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        var pageModel = new TeamDraftPageModel(client.Object, Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        return pageModel;
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
    public async Task SelectGame_LoadsTheTappedGameAndRefreshesCountsAndRoster()
    {
        var gameA = Guid.NewGuid();
        var gameB = Guid.NewGuid();
        var options = new[]
        {
            new GameDayGameOptionDto(gameA, "Game A", "Venue A", new DateTime(2026, 7, 24, 20, 0, 0, DateTimeKind.Utc), "Open", true),
            new GameDayGameOptionDto(gameB, "Game B", "Venue B", new DateTime(2026, 7, 24, 22, 0, 0, DateTimeKind.Utc), "Open", false),
        };
        var rosterA = new[]
        {
            new GameDayRosterEntryDto(Guid.NewGuid(), "Alice", false, false, false),
            new GameDayRosterEntryDto(Guid.NewGuid(), "Bob", false, false, false),
            new GameDayRosterEntryDto(Guid.NewGuid(), "Cara", false, true, false),
        };
        var contextA = MakeContext(gameA, "Game A", goingCount: 2, roster: rosterA, todaysGames: options);
        var contextB = MakeContext(
            gameB,
            "Game B",
            goingCount: 0,
            roster: [],
            todaysGames: [.. options.Select(o => o with { IsSelected = o.SessionId == gameB })]);
        var client = new Mock<IGameDayClient>();
        client.Setup(c => c.GetTodayContextAsync(null, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(contextA);
        client.Setup(c => c.GetTodayContextAsync(gameB, It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(contextB);
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.GoingCount.Should().Be(2);
        pageModel.WaitlistCount.Should().Be(1);

        await pageModel.SelectGameCommand.ExecuteAsync(gameB);

        pageModel.GoingCount.Should().Be(0, "switching to an empty game must refresh its counts");
        pageModel.WaitlistCount.Should().Be(0);
        pageModel.CheckedInCount.Should().Be(0);
        pageModel.Roster.Should().BeEmpty();
        client.Verify(c => c.GetTodayContextAsync(gameB, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Appearing_WhenSpectatorContext_ShowsReadOnlyViewWithBannerAndJoin()
    {
        var context = SpectatorContext();
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.IsSpectator.Should().BeTrue();
        pageModel.IsParticipant.Should().BeFalse();
        pageModel.SpectatorBannerText.Should().Contain("Bay Area Soccer");
        pageModel.HeaderContextLabel.Should().Be("Bay Area Soccer · Marina Field");
        pageModel.CanJoin.Should().BeTrue();
        pageModel.Capacity.Should().Be(20);
        pageModel.HasGameDayActions.Should().BeFalse();
        pageModel.CanCheckIn.Should().BeFalse();
        pageModel.CanManageCheckIns.Should().BeFalse();
        pageModel.Roster.Should().OnlyContain(item => !item.CanCheckIn, "spectator popups are read-only");
    }

    [Fact]
    public async Task Appearing_WhenSpectatorPayloadStillCarriesActionFlags_HardensThemOff()
    {
        // Defense in depth: even if a stale server left action flags on, spectator zeroes them.
        var context = SpectatorContext() with
        {
            IsSelfCheckInAvailable = true,
            CanAssignCaptains = true,
            CanDraftTeam = true,
            CanApprovePostGame = true,
            CanSubmitOwnStats = true,
            CanLateCheckIn = true,
            CanManageCheckIns = true,
            CanViewTeams = true,
        };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.CanCheckIn.Should().BeFalse();
        pageModel.CanAssignCaptains.Should().BeFalse();
        pageModel.CanDraftTeam.Should().BeFalse();
        pageModel.CanApprovePostGame.Should().BeFalse();
        pageModel.CanSubmitOwnStats.Should().BeFalse();
        pageModel.CanLateCheckIn.Should().BeFalse();
        pageModel.CanManageCheckIns.Should().BeFalse();
        pageModel.CanViewTeams.Should().BeFalse();
    }

    [Fact]
    public async Task Join_WhenRsvpSucceeds_ReloadsAndBecomesParticipant()
    {
        var spectator = SpectatorContext();
        var participant = new SeedGameDayState().GetContext();
        var client = new Mock<IGameDayClient>();
        var loads = 0;
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++loads == 1 ? spectator : participant);
        var roster = new Mock<IRosterClient>();
        roster
            .Setup(r => r.SetRsvpIntentAsync(spectator.SessionId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Success);
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object, rosterClient: roster.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.JoinCommand.ExecuteAsync(null);

        roster.Verify(r => r.SetRsvpIntentAsync(spectator.SessionId, true, It.IsAny<CancellationToken>()), Times.Once);
        pageModel.IsSpectator.Should().BeFalse("the reloaded context is the participant view");
        pageModel.State.Should().Be(ViewState.Content);
    }

    [Fact]
    public async Task Join_WhenRsvpFails_ShowsDialogAndStaysOnContent()
    {
        var spectator = SpectatorContext();
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(spectator);
        var roster = new Mock<IRosterClient>();
        roster
            .Setup(r => r.SetRsvpIntentAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Failure("waiver_required", "Waiver required."));
        var dialogs = new Mock<IUserDialogService>();
        var pageModel = new GameDayPageModel(
            client.Object, Navigator().Object, dialogService: dialogs.Object, rosterClient: roster.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.JoinCommand.ExecuteAsync(null);

        dialogs.Verify(
            d => d.ShowAlertAsync(GameDayPageModel.JoinFailedTitle, "Waiver required.", "OK"),
            Times.Once);
        pageModel.State.Should().Be(ViewState.Content, "a failed CTA never blanks the page");
        pageModel.IsSpectator.Should().BeTrue();
        client.Verify(
            c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "no reload after a failed join");
    }

    [Fact]
    public async Task ToggleAllGames_WhenAdmin_ReloadsWithAllFlagAndClearsPinnedSession()
    {
        var context = new SeedGameDayState().GetContext(); // CanShowAllGames: true in seed
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.ToggleAllGamesCommand.ExecuteAsync(null);

        client.Verify(c => c.GetTodayContextAsync(null, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Appearing_WhenNoGameButLastGameExists_ShowsNoGameSummary()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDayContextDto?)null);
        client
            .Setup(c => c.GetLastGameSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LastGameSummaryDto(
                Guid.NewGuid(), "Bay Area - Wednesday pickup", "Bay Area Soccer", "Marina Field",
                "Wed Jul 22, 7:30 PM", new DateTime(2026, 7, 23, 2, 30, 0, DateTimeKind.Utc),
                14, 12, 2, "Team Vic 2W · Team Ade 1W 1D"));
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.IsNoGame.Should().BeTrue();
        pageModel.IsParticipant.Should().BeFalse();
        pageModel.HasLastGame.Should().BeTrue();
        pageModel.Title.Should().Be(GameDayPageModel.NoGameTitle);
        pageModel.LastGameCountsLabel.Should().Be("14 going · 12 checked in · 2 teams");
    }

    [Fact]
    public async Task LastGame_TeamTap_ShowsMembersWithGoalTalliesInThePopup()
    {
        var captainId = Guid.NewGuid();
        var scorerId = Guid.NewGuid();
        var team = new LastGameTeamDto(
            Guid.NewGuid(),
            "Team Vic",
            "Victor",
            "2W",
            [
                new LastGameTeamMemberDto(captainId, "Victor", IsCaptain: true, Goals: 2, Assists: 1),
                new LastGameTeamMemberDto(scorerId, "Ada", IsCaptain: false, Goals: 1, Assists: 2),
                new LastGameTeamMemberDto(Guid.NewGuid(), "Tunde", IsCaptain: false, Goals: 0),
            ]);
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDayContextDto?)null);
        client
            .Setup(c => c.GetLastGameSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LastGameWith(teams: [team]));
        var presenter = new Mock<IRosterListPresenter>();
        IReadOnlyList<GameDayRosterItem>? shown = null;
        presenter
            .Setup(p => p.ShowAsync(
                "Team Vic",
                It.IsAny<IReadOnlyList<GameDayRosterItem>>(),
                It.IsAny<System.Windows.Input.ICommand>(),
                It.IsAny<System.Windows.Input.ICommand>()))
            .Callback<string, IReadOnlyList<GameDayRosterItem>, System.Windows.Input.ICommand, System.Windows.Input.ICommand>(
                (_, members, _, _) => shown = members)
            .Returns(Task.CompletedTask);
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object, presenter.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.ShowLastGameTeamCommand.ExecuteAsync(team);

        pageModel.HasLastGameTeams.Should().BeTrue();
        shown.Should().NotBeNull();
        shown![0].DisplayName.Should().Be("Victor");
        shown[0].StatusLabel.Should().Be("Captain · ⚽⚽ · 🦶");
        shown[1].StatusLabel.Should().Be("⚽ · 🦶🦶");
        shown[2].StatusLabel.Should().BeEmpty();
        shown.Should().OnlyContain(item => !item.CanCheckIn, "the recap popup is read-only");
    }

    [Fact]
    public async Task LastGame_FollowUpActions_NavigateToTheRightScreensForTheLastSession()
    {
        var sessionId = Guid.NewGuid();
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDayContextDto?)null);
        client
            .Setup(c => c.GetLastGameSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LastGameWith(sessionId: sessionId, canLock: true, canMatch: true, canApprove: true));
        var navigator = Navigator();
        navigator.Setup(n => n.OpenAdminMatchAsync(sessionId)).Returns(Task.CompletedTask);
        navigator.Setup(n => n.OpenPostGameApprovalAsync(sessionId)).Returns(Task.CompletedTask);
        var pageModel = new GameDayPageModel(client.Object, navigator.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.HasLastGameActions.Should().BeTrue();
        await pageModel.OpenLastGameCaptainsCommand.ExecuteAsync(null);
        await pageModel.OpenLastGameMatchPlayersCommand.ExecuteAsync(null);
        await pageModel.OpenLastGameApprovalCommand.ExecuteAsync(null);

        navigator.Verify(n => n.OpenCaptainAssignmentAsync(sessionId), Times.Once);
        navigator.Verify(n => n.OpenAdminMatchAsync(sessionId), Times.Once);
        navigator.Verify(n => n.OpenPostGameApprovalAsync(sessionId), Times.Once);
    }

    [Fact]
    public async Task LastGame_RegularPlayer_SeesNoFollowUpActionsAndCannotNavigate()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDayContextDto?)null);
        client
            .Setup(c => c.GetLastGameSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LastGameWith());
        var navigator = Navigator();
        var pageModel = new GameDayPageModel(client.Object, navigator.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.HasLastGameActions.Should().BeFalse();
        pageModel.OpenLastGameRatingsCommand.CanExecute(null).Should().BeFalse();
        await pageModel.OpenLastGameCaptainsCommand.ExecuteAsync(null);
        await pageModel.OpenLastGameApprovalCommand.ExecuteAsync(null);
        await pageModel.OpenLastGameRatingsCommand.ExecuteAsync(null);

        navigator.Verify(n => n.OpenCaptainAssignmentAsync(It.IsAny<Guid>()), Times.Never);
        navigator.Verify(n => n.OpenPostGameApprovalAsync(It.IsAny<Guid>()), Times.Never);
        navigator.Verify(n => n.OpenRateTeammatesAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task LastGame_RegularPlayer_CanOpenExistingRateTeammatesPage()
    {
        var matchId = Guid.NewGuid();
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDayContextDto?)null);
        client
            .Setup(c => c.GetLastGameSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LastGameWith(matchId: matchId, canRate: true));
        var navigator = Navigator();
        var pageModel = new GameDayPageModel(client.Object, navigator.Object);
        var canExecuteChanged = false;
        pageModel.OpenLastGameRatingsCommand.CanExecuteChanged += (_, _) => canExecuteChanged = true;
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.HasLastGameActions.Should().BeTrue();
        pageModel.LastGameCanRateTeammates.Should().BeTrue();
        canExecuteChanged.Should().BeTrue("the already-bound rating row must be notified when LastGame loads");
        pageModel.OpenLastGameRatingsCommand.CanExecute(null).Should().BeTrue();
        await pageModel.OpenLastGameRatingsCommand.ExecuteAsync(null);

        navigator.Verify(n => n.OpenRateTeammatesAsync(matchId), Times.Once);
    }

    [Fact]
    public async Task LastGameCountsLabel_IncludesWaitlistWhenPresent()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDayContextDto?)null);
        client
            .Setup(c => c.GetLastGameSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LastGameWith(waitlistCount: 26));
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.LastGameCountsLabel.Should().Be("22 going · 26 waitlist · 11 checked in · 2 teams");
    }

    private static LastGameSummaryDto LastGameWith(
        Guid? sessionId = null,
        int waitlistCount = 0,
        IReadOnlyList<LastGameTeamDto>? teams = null,
        bool canLock = false,
        bool canMatch = false,
        bool canApprove = false,
        Guid? matchId = null,
        bool canRate = false) =>
        new(
            sessionId ?? Guid.NewGuid(),
            "Bay Area - Wednesday pickup",
            "Bay Area Soccer",
            "Marina Field",
            "Wed Jul 22, 7:30 PM",
            new DateTime(2026, 7, 23, 2, 30, 0, DateTimeKind.Utc),
            GoingCount: 22,
            CheckedInCount: 11,
            TeamCount: 2,
            ResultSummary: null,
            WaitlistCount: waitlistCount,
            Teams: teams,
            CanLockTeams: canLock,
            CanMatchPlayers: canMatch,
            CanApprovePostGame: canApprove,
            MatchId: matchId ?? Guid.Empty,
            CanRateTeammates: canRate);

    [Fact]
    public async Task Appearing_WhenNoGameAndNoHistory_ShowsEmptyState()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDayContextDto?)null);
        client
            .Setup(c => c.GetLastGameSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((LastGameSummaryDto?)null);
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.StateTitle.Should().Be(GameDayPageModel.NoGameTitle);
        pageModel.StateMessage.Should().Be(GameDayPageModel.NoGameMessage);
        pageModel.IsNoGame.Should().BeFalse();
    }

    [Fact]
    public async Task Appearing_WhenLastGameLookupFails_DegradesToEmptyState()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDayContextDto?)null);
        client
            .Setup(c => c.GetLastGameSummaryAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty, "the summary is garnish; its failure never errors the page");
    }

    [Fact]
    public async Task StatusBadgeVariant_MapsStateToVariant()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SpectatorContext() with { Status = GameDayStatus.Blocked });
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        // The server maps spectators onto Blocked; the badge must stay neutral, not amber.
        pageModel.StatusBadgeVariant.Should().Be(SouthBaySoccer.Controls.BadgeVariant.Neutral);

        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeedGameDayState().GetContext());
        await pageModel.RetryCommand.ExecuteAsync(null);
        pageModel.StatusBadgeVariant.Should().Be(SouthBaySoccer.Controls.BadgeVariant.Success, "participant Open maps to Success");
    }

    [Fact]
    public async Task Appearing_WhenAdminHasNoRelevantGame_StillOffersTheAllGamesSwitch()
    {
        // A 204 today-context carries no flags, so admin-ness comes from the cached profile role;
        // without this the switch is unreachable exactly when an admin needs it.
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDayContextDto?)null);
        client
            .Setup(c => c.GetLastGameSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((LastGameSummaryDto?)null);
        var profile = new Mock<IProfileClient>();
        profile
            .Setup(p => p.GetCurrentProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SouthBaySoccer.Contracts.Profiles.PlayerProfileDto(
                Guid.NewGuid(), "Ada", "Player", "AD",
                new SouthBaySoccer.Contracts.Profiles.CareerStatsDto(0, 0, 0, 0, 0, 0), [], null, Role: "GameAdmin"));
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object, profileClient: profile.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content, "the switch needs rendered content, not the empty overlay");
        pageModel.IsNoGame.Should().BeTrue();
        pageModel.CanShowAllGames.Should().BeTrue();

        await pageModel.ToggleAllGamesCommand.ExecuteAsync(null);

        client.Verify(c => c.GetTodayContextAsync(null, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Appearing_WhenNonAdminHasNoGameAndNoHistory_KeepsBareEmptyState()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameDayContextDto?)null);
        client
            .Setup(c => c.GetLastGameSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((LastGameSummaryDto?)null);
        var profile = new Mock<IProfileClient>();
        profile
            .Setup(p => p.GetCurrentProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SouthBaySoccer.Contracts.Profiles.PlayerProfileDto(
                Guid.NewGuid(), "Ada", "Player", "AD",
                new SouthBaySoccer.Contracts.Profiles.CareerStatsDto(0, 0, 0, 0, 0, 0), [], null, Role: "Player"));
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object, profileClient: profile.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.CanShowAllGames.Should().BeFalse();
    }

    private static GameDayContextDto SpectatorContext()
    {
        var baseContext = new SeedGameDayState().GetContext();
        return baseContext with
        {
            Title = "Bay Area - Wednesday pickup",
            GroupName = "Bay Area Soccer",
            IsSpectator = true,
            CanJoin = true,
            Capacity = 20,
            StatusLabel = "Spectator",
            IsSelfCheckInAvailable = false,
            CanAssignCaptains = false,
            CanDraftTeam = false,
            CanApprovePostGame = false,
            CanSubmitOwnStats = false,
            CanLateCheckIn = false,
            CanManageCheckIns = false,
            CanViewTeams = false,
            IsCurrentPlayerGoing = false,
        };
    }

    private static GameDayContextDto MakeContext(
        Guid sessionId,
        string title,
        int goingCount,
        IReadOnlyList<GameDayRosterEntryDto> roster,
        IReadOnlyList<GameDayGameOptionDto> todaysGames,
        bool canAssignCaptains = false,
        bool canDraftTeam = false,
        bool canViewTeams = false) =>
        new(
            sessionId, Guid.NewGuid(), title, "Venue", "Fri Jul 24",
            "3:00 PM", "2:50 PM - 3:00 PM", "closes 3:00 PM",
            GameDayStatus.Open, "Open", false, "Check in", null, "Going",
            false, false, goingCount, 0, 0, canAssignCaptains, canDraftTeam, false,
            Roster: roster, CanManageCheckIns: true, TodaysGames: todaysGames, CanViewTeams: canViewTeams);

    private static GameDayContextDto SingleGameContext(
        bool canAssignCaptains,
        bool canDraftTeam,
        bool canViewTeams = false)
    {
        var game = Guid.NewGuid();
        return MakeContext(
            game,
            "Game",
            goingCount: 5,
            roster: [],
            todaysGames: [new GameDayGameOptionDto(game, "Game", "Venue", new DateTime(2026, 7, 24, 20, 0, 0, DateTimeKind.Utc), "Open", true)],
            canAssignCaptains: canAssignCaptains,
            canDraftTeam: canDraftTeam,
            canViewTeams: canViewTeams);
    }

    [Fact]
    public async Task ViewTeams_RegularPlayer_ShowsEntryAndNavigatesToTeamsView()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleGameContext(canAssignCaptains: false, canDraftTeam: false, canViewTeams: true));
        var navigator = Navigator();
        var pageModel = new GameDayPageModel(client.Object, navigator.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.ShowViewTeams.Should().BeTrue();
        pageModel.ShowPickTeam.Should().BeFalse();

        await pageModel.OpenTeamsViewCommand.ExecuteAsync(null);

        navigator.Verify(n => n.OpenTeamsViewAsync(It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task ViewTeams_HiddenForAdminWhoCanDraft()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleGameContext(canAssignCaptains: true, canDraftTeam: true, canViewTeams: true));
        var pageModel = new GameDayPageModel(client.Object, Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.ShowPickTeam.Should().BeTrue();
        pageModel.ShowViewTeams.Should().BeFalse("captains and admins use the draft screen, not the read-only view");
    }

    [Fact]
    public async Task CaptainAssignment_LockButton_StaysVisibleWithHintUntilLockable()
    {
        // The Lock button used to hide entirely until lockable, leaving admins hunting for it.
        var checkedIn = new[]
        {
            new CheckedInPlayerDto(new PlayerSummaryDto(Guid.NewGuid(), "Ada", "A", "Midfielder", false), "going"),
        };
        var dto = new CaptainAssignmentDto(
            Guid.NewGuid(), Guid.NewGuid(), 2, new[] { 2, 3, 4 },
            [], checkedIn, CanLockTeams: false, IsLocked: false, CanUnlockTeams: false);
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetCaptainAssignmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        var pageModel = new CaptainAssignmentPageModel(client.Object, Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.ShowLockTeams.Should().BeTrue("the button is visible but disabled");
        pageModel.ShowLockHint.Should().BeTrue("the hint explains what unlocks it");
        pageModel.LockTeamsCommand.CanExecute(null).Should().BeFalse();

        // Once lockable the hint drops and the command enables; once locked both disappear.
        var lockable = dto with { CanLockTeams = true };
        client
            .Setup(c => c.GetCaptainAssignmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockable);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.ShowLockTeams.Should().BeTrue();
        pageModel.ShowLockHint.Should().BeFalse();
        pageModel.LockTeamsCommand.CanExecute(null).Should().BeTrue();

        var locked = dto with { IsLocked = true };
        client
            .Setup(c => c.GetCaptainAssignmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(locked);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.ShowLockTeams.Should().BeFalse();
        pageModel.ShowLockHint.Should().BeFalse();
    }

    [Fact]
    public async Task CaptainAssignment_GrantRejectedByServer_ShowsTheReason()
    {
        // A rejected grant used to fail silently — tap, nothing happens, no reason shown.
        var players = new[]
        {
            new CheckedInPlayerDto(new PlayerSummaryDto(Guid.NewGuid(), "Ada", "A", "Midfielder", false), "going"),
            new CheckedInPlayerDto(new PlayerSummaryDto(Guid.NewGuid(), "Vic", "V", "Forward", false), "going"),
        };
        var dto = new CaptainAssignmentDto(
            Guid.NewGuid(), Guid.NewGuid(), 2, new[] { 2, 3, 4 },
            [], players, CanLockTeams: false, IsLocked: false, CanUnlockTeams: false);
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetCaptainAssignmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        client
            .Setup(c => c.AssignCaptainsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Failure(
                "conflict", "Team count cannot change after results or stats have been recorded."));
        var dialogs = new Mock<IUserDialogService>();
        var pageModel = new CaptainAssignmentPageModel(client.Object, Navigator().Object, dialogs.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        foreach (var item in pageModel.Players)
        {
            pageModel.ToggleCaptainCommand.Execute(item);
        }

        await pageModel.GrantCommand.ExecuteAsync(null);

        dialogs.Verify(
            d => d.ShowAlertAsync(
                CaptainAssignmentPageModel.GrantFailedTitle,
                "Team count cannot change after results or stats have been recorded.",
                "OK"),
            Times.Once);
    }

    [Fact]
    public async Task CaptainAssignment_UnlockTeams_CallsClientWhenAvailable()
    {
        var checkedIn = new[]
        {
            new CheckedInPlayerDto(new PlayerSummaryDto(Guid.NewGuid(), "Ada", "A", "Midfielder", false), "going"),
        };
        var dto = new CaptainAssignmentDto(
            Guid.NewGuid(), Guid.NewGuid(), 2, new[] { 2, 3, 4 },
            [], checkedIn, CanLockTeams: false, IsLocked: true, CanUnlockTeams: true);
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetCaptainAssignmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        client
            .Setup(c => c.UnlockTeamsAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Success);
        var pageModel = new CaptainAssignmentPageModel(client.Object, Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.CanUnlockTeams.Should().BeTrue();
        pageModel.UnlockTeamsCommand.CanExecute(null).Should().BeTrue();

        await pageModel.UnlockTeamsCommand.ExecuteAsync(null);

        client.Verify(c => c.UnlockTeamsAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OpenTeamDraft_WithoutCaptains_WarnsAndDoesNotNavigate()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleGameContext(canAssignCaptains: true, canDraftTeam: false));
        var navigator = Navigator();
        var dialog = new Mock<IUserDialogService>();
        var pageModel = new GameDayPageModel(client.Object, navigator.Object, null, dialog.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.ShowPickTeam.Should().BeTrue("admins see the entry during setup so the guard can explain");

        await pageModel.OpenTeamDraftCommand.ExecuteAsync(null);

        dialog.Verify(
            d => d.ShowAlertAsync(GameDayPageModel.NoCaptainsTitle, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        navigator.Verify(n => n.OpenTeamDraftAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task OpenTeamDraft_WithCaptains_Navigates()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(c => c.GetTodayContextAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SingleGameContext(canAssignCaptains: true, canDraftTeam: true));
        var navigator = Navigator();
        var pageModel = new GameDayPageModel(client.Object, navigator.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.OpenTeamDraftCommand.ExecuteAsync(null);

        navigator.Verify(n => n.OpenTeamDraftAsync(It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task Appearing_SeedContext_DerivesWaitlistCountAndCategorySlices()
    {
        var pageModel = new GameDayPageModel(
            new SeedGameDayClient(new SeedGameDayState()),
            Navigator().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.WaitlistCount.Should().Be(pageModel.Roster.Count(item => item.IsWaitlist));
        pageModel.WaitlistCount.Should().BeGreaterThan(0);
        pageModel.GoingRoster.Should().OnlyContain(item => !item.IsWaitlist);
        pageModel.WaitlistRoster.Should().OnlyContain(item => item.IsWaitlist);
        pageModel.CheckedInRoster.Should().OnlyContain(item => item.IsCheckedIn);
    }

    [Theory]
    [InlineData("Going", "Going")]
    [InlineData("Waitlist", "Waitlist")]
    [InlineData("CheckedIn", "Checked in")]
    public async Task ShowRoster_OpensPopupWithTheMatchingCategoryList(string category, string expectedTitle)
    {
        var presenter = new Mock<IRosterListPresenter>();
        var pageModel = new GameDayPageModel(
            new SeedGameDayClient(new SeedGameDayState()),
            Navigator().Object,
            presenter.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        var expected = category switch
        {
            "Waitlist" => pageModel.WaitlistRoster,
            "CheckedIn" => pageModel.CheckedInRoster,
            _ => pageModel.GoingRoster,
        };

        await pageModel.ShowRosterCommand.ExecuteAsync(category);

        presenter.Verify(
            p => p.ShowAsync(
                expectedTitle,
                It.Is<IReadOnlyList<GameDayRosterItem>>(list => list.Count == expected.Count),
                It.IsAny<ICommand>(),
                It.IsAny<ICommand>()),
            Times.Once);
    }

    [Fact]
    public void RosterItem_WhenUnlinkedAndCallerManagesRoster_OffersMatchNotClaim()
    {
        var item = new GameDayRosterItem(
            playerProfileId: null,
            "victor",
            isGuest: false,
            isWaitlist: true,
            isCheckedIn: false,
            canCheckIn: false,
            "Not linked to a profile",
            pickupPalParticipantId: "pp-victor",
            canManageRoster: true);

        item.IsUnlinked.Should().BeTrue();
        item.CanMatch.Should().BeTrue();
        item.CanClaim.Should().BeFalse("an admin links other people, they do not claim them");
    }

    [Fact]
    public void RosterItem_WhenUnlinkedAndCallerIsAPlayer_OffersClaimNotMatch()
    {
        var item = new GameDayRosterItem(
            playerProfileId: null,
            "victor",
            isGuest: false,
            isWaitlist: true,
            isCheckedIn: false,
            canCheckIn: false,
            "Not linked to a profile",
            pickupPalParticipantId: "pp-victor",
            canManageRoster: false);

        item.CanClaim.Should().BeTrue();
        item.CanMatch.Should().BeFalse("only an admin or captain may link somebody else");
    }

    [Fact]
    public async Task LinkRosterMember_WhenLinked_DoesNothing()
    {
        var navigator = Navigator();
        var pageModel = new GameDayPageModel(
            new SeedGameDayClient(new SeedGameDayState()),
            navigator.Object);
        var linked = new GameDayRosterItem(
            Guid.NewGuid(), "Bola", false, true, false, false, "Waitlist");

        await pageModel.LinkRosterMemberCommand.ExecuteAsync(linked);

        navigator.Verify(x => x.OpenAdminMatchAsync(It.IsAny<Guid>()), Times.Never);
        navigator.Verify(x => x.OpenClaimSpotAsync(), Times.Never);
    }

    [Fact]
    public async Task ShowRoster_PassesTheAdminCheckInCommandSoThePopupCanCheckPeopleIn()
    {
        var presenter = new Mock<IRosterListPresenter>();
        var pageModel = new GameDayPageModel(
            new SeedGameDayClient(new SeedGameDayState()),
            Navigator().Object,
            presenter.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.ShowRosterCommand.ExecuteAsync("Going");

        presenter.Verify(
            p => p.ShowAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<GameDayRosterItem>>(),
                pageModel.AdminCheckInCommand,
                pageModel.LinkRosterMemberCommand),
            Times.Once);
    }

    [Fact]
    public async Task ShowRoster_WhenNoPresenterConfigured_DoesNothing()
    {
        var pageModel = new GameDayPageModel(
            new SeedGameDayClient(new SeedGameDayState()),
            Navigator().Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        var act = async () => await pageModel.ShowRosterCommand.ExecuteAsync("Going");

        await act.Should().NotThrowAsync();
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
        // The inline roster list was replaced by tappable count tiles that open a roster popup.
        gameDay.Should().Contain("ShowRosterCommand");
        captains.Should().Contain("PlayerRow").And.Contain("CheckBox");
        draft.Should().Contain("Captain pick", "the badge is a human label, not implementation terminology")
            .And.Contain("PlayerRow");
        draft.Should().Contain("SelectTeamCommand");
        postgame.Should().Contain("CounterStepper").And.Contain("Publish approved match");
        (gameDay + captains + draft + postgame).Should().Contain("FontAwesomeGlyphs");
    }

    [Fact]
    public void GameDayXaml_RendersThreeModesWithSpectatorJoinAndNoGameSummary()
    {
        var gameDay = LoadXaml("GameDayPage.xaml").ToString();

        // Shared header is descriptive (group · venue, session title, state-driven badge).
        gameDay.Should().Contain("HeaderContextLabel");
        gameDay.Should().Contain("{Binding Title}");
        gameDay.Should().NotContain("Variant=\"Success\"", "the badge variant is computed on the page model");
        gameDay.Should().Contain("StatusBadgeVariant");
        // Spectator mode: banner + read-only counts + a single Join CTA with capacity context.
        gameDay.Should().Contain("SpectatorBannerText");
        gameDay.Should().Contain("JoinCommand");
        gameDay.Should().Contain("CapacityBar");
        gameDay.Should().Contain("JoinBlockedReason");
        // Participant machinery is scoped to the participant container.
        gameDay.Should().Contain("IsParticipant");
        // Admin scope switch.
        gameDay.Should().Contain("SegmentedControl");
        gameDay.Should().Contain("SelectGameScopeCommand");
        // No-game mode with the last-game summary card.
        gameDay.Should().Contain("IsNoGame");
        gameDay.Should().Contain("LastGameCountsLabel");
        gameDay.Should().Contain("HasLastGameResult");
        // The hero button uses the catalog style, not page-local colors.
        gameDay.Should().Contain("HeroInverseButton");
        gameDay.Should().NotContain("BackgroundColor=\"White\"", "page-local colors violate the control catalog");
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
