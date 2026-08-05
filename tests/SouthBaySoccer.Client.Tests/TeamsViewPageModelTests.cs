using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Client.Tests.TestSupport;

namespace SouthBaySoccer.Client.Tests;

public class TeamsViewPageModelTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    [Fact]
    public async Task Appearing_LoadsTeamsWithMembers()
    {
        var teams = new[]
        {
            new SessionTeamDto(Guid.NewGuid(), "Team Ada", "Ada", true,
            [
                new SessionTeamMemberDto(Guid.NewGuid(), "Ada", true, true),
                new SessionTeamMemberDto(Guid.NewGuid(), "Ben", false, false),
            ]),
            new SessionTeamDto(Guid.NewGuid(), "Team Cara", "Cara", false,
            [
                new SessionTeamMemberDto(Guid.NewGuid(), "Cara", true, false),
            ]),
        };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(x => x.GetSessionTeamsAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionTeamsDto(SessionId, Guid.NewGuid(), teams));
        var model = CreateModel(client);

        await model.AppearingCommand.ExecuteAsync(null);

        model.State.Should().Be(ViewState.Content);
        model.Teams.Should().HaveCount(2);
        model.Teams.Should().ContainSingle(t => t.IsMine);
    }

    [Fact]
    public async Task Appearing_MidDraft_ShowsBannerAndPlayersYetToBePicked()
    {
        var teams = new[]
        {
            new SessionTeamDto(Guid.NewGuid(), "Team Ada", "Ada", true,
            [
                new SessionTeamMemberDto(Guid.NewGuid(), "Ada", true, true),
            ]),
        };
        var client = new Mock<IGameDayClient>();
        client
            .Setup(x => x.GetSessionTeamsAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionTeamsDto(
                SessionId,
                Guid.NewGuid(),
                teams,
                IsDraftInProgress: true,
                OnTheClockLabel: "On the clock: Team Ada",
                AvailablePlayers:
                [
                    new SessionTeamMemberDto(Guid.NewGuid(), "Bench One", false, false),
                    new SessionTeamMemberDto(Guid.NewGuid(), "Bench Two", false, true),
                ]));
        var model = CreateModel(client);

        await model.AppearingCommand.ExecuteAsync(null);

        model.IsDraftInProgress.Should().BeTrue();
        model.OnTheClockLabel.Should().Be("On the clock: Team Ada");
        model.HasAvailablePlayers.Should().BeTrue();
        model.AvailableHeader.Should().Be("Yet to be picked (2)");
        model.AvailablePlayers.Should().ContainSingle(player => player.IsMe);
    }

    [Fact]
    public async Task Appearing_SettledTeams_HidesDraftChrome()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(x => x.GetSessionTeamsAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionTeamsDto(
                SessionId,
                Guid.NewGuid(),
                [new SessionTeamDto(Guid.NewGuid(), "Team Ada", "Ada", true, [])]));
        var model = CreateModel(client);

        await model.AppearingCommand.ExecuteAsync(null);

        model.IsDraftInProgress.Should().BeFalse();
        model.HasAvailablePlayers.Should().BeFalse();
    }

    [Fact]
    public async Task Appearing_WhenNoTeams_ShowsEmpty()
    {
        var client = new Mock<IGameDayClient>();
        client
            .Setup(x => x.GetSessionTeamsAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionTeamsDto(SessionId, Guid.NewGuid(), []));
        var model = CreateModel(client);

        await model.AppearingCommand.ExecuteAsync(null);

        model.State.Should().Be(ViewState.Empty);
        model.Teams.Should().BeEmpty();
    }

    [Fact]
    public async Task Polling_WhenDraftSettles_AppliesInBackgroundAndStops()
    {
        var initial = new SessionTeamsDto(
            SessionId,
            Guid.NewGuid(),
            [new SessionTeamDto(Guid.NewGuid(), "Team Ada", "Ada", true, [])],
            IsDraftInProgress: true,
            DraftRevision: 4);
        var settled = initial with { IsDraftInProgress = false, DraftRevision = 5 };
        var client = new Mock<IGameDayClient>();
        client.Setup(x => x.GetSessionTeamsAsync(SessionId, It.IsAny<CancellationToken>())).ReturnsAsync(initial);
        client.Setup(x => x.GetSessionTeamsIfChangedAsync(SessionId, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConditionalReadResult<SessionTeamsDto>(true, 5, settled));
        var delay = new ControlledPollingDelay();
        var model = new TeamsViewPageModel(client.Object, new Mock<IGameDayNavigator>().Object, delay);
        model.ApplyQueryAttributes(new Dictionary<string, object> { ["sessionId"] = SessionId.ToString() });

        await model.AppearingCommand.ExecuteAsync(null);
        await delay.WaitForDelayCountAsync(1);
        delay.Delays[0].Should().Be(TimeSpan.FromSeconds(5));
        delay.ReleaseNext();

        for (var attempt = 0; attempt < 100 && model.IsDraftInProgress; attempt++)
        {
            await Task.Delay(10);
        }

        model.IsDraftInProgress.Should().BeFalse();
        model.State.Should().Be(ViewState.Content, "background refresh must not flash Loading");
        await model.DisappearingCommand.ExecuteAsync(null);
        client.Verify(x => x.GetSessionTeamsIfChangedAsync(SessionId, 4, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TeamsViewPageModel CreateModel(Mock<IGameDayClient> client)
    {
        var model = new TeamsViewPageModel(client.Object, new Mock<IGameDayNavigator>().Object);
        model.ApplyQueryAttributes(new Dictionary<string, object> { ["sessionId"] = SessionId.ToString() });
        return model;
    }
}
