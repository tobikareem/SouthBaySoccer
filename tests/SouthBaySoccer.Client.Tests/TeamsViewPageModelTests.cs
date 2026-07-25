using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Clients;

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

    private static TeamsViewPageModel CreateModel(Mock<IGameDayClient> client)
    {
        var model = new TeamsViewPageModel(client.Object, new Mock<IGameDayNavigator>().Object);
        model.ApplyQueryAttributes(new Dictionary<string, object> { ["sessionId"] = SessionId.ToString() });
        return model;
    }
}
