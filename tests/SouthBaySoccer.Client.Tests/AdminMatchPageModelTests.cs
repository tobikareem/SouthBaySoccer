using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public class AdminMatchPageModelTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid ParticipantId = Guid.NewGuid();
    private static readonly Guid VicProfileId = Guid.NewGuid();

    [Fact]
    public async Task Appearing_ListsUnlinkedEntriesAndLoadsDirectory()
    {
        var model = CreateModel(out _, out _);

        await model.AppearingCommand.ExecuteAsync(null);

        model.State.Should().Be(ViewState.Content);
        model.ShowUnlinked.Should().BeTrue();
        model.ShowPicker.Should().BeFalse();
        model.Unlinked.Should().ContainSingle(e => e.DisplayName == "victor");
    }

    [Fact]
    public async Task SelectEntry_ThenSearch_FiltersTheDirectory()
    {
        var model = CreateModel(out _, out _);
        await model.AppearingCommand.ExecuteAsync(null);

        model.SelectEntryCommand.Execute(model.Unlinked[0]);

        model.ShowPicker.Should().BeTrue();
        model.PickerPrompt.Should().Contain("victor");
        model.Candidates.Should().HaveCount(2);

        model.SearchText = "vic";
        model.Candidates.Should().ContainSingle(c => c.Player.DisplayName == "Vic A");
    }

    [Fact]
    public async Task LinkTo_OnSuccess_LinksAndRemovesTheEntry()
    {
        var model = CreateModel(out var gameDay, out _);
        await model.AppearingCommand.ExecuteAsync(null);
        model.SelectEntryCommand.Execute(model.Unlinked[0]);
        var target = model.Candidates.First(c => c.Player.Id == VicProfileId);

        await model.LinkToCommand.ExecuteAsync(target);

        gameDay.Verify(x => x.LinkParticipantAsync(ParticipantId, VicProfileId, It.IsAny<CancellationToken>()), Times.Once);
        model.ShowPicker.Should().BeFalse();
        // Only entry was matched, so the page empties out.
        model.State.Should().Be(ViewState.Empty);
    }

    [Fact]
    public async Task Appearing_WhenNothingUnlinked_ShowsEmpty()
    {
        var gameDay = new Mock<IGameDayClient>();
        gameDay.Setup(x => x.GetUnlinkedParticipantsAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var model = new AdminMatchPageModel(gameDay.Object, new Mock<IPlayersClient>().Object, new Mock<IAdminMatchNavigator>().Object);
        model.ApplyQueryAttributes(new Dictionary<string, object> { ["sessionId"] = SessionId.ToString() });

        await model.AppearingCommand.ExecuteAsync(null);

        model.State.Should().Be(ViewState.Empty);
    }

    private static AdminMatchPageModel CreateModel(
        out Mock<IGameDayClient> gameDay,
        out Mock<IPlayersClient> players)
    {
        gameDay = new Mock<IGameDayClient>();
        gameDay.Setup(x => x.GetUnlinkedParticipantsAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClaimableParticipantDto(ParticipantId, "victor", IsWaitlist: true)]);
        gameDay.Setup(x => x.LinkParticipantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Success);

        players = new Mock<IPlayersClient>();
        players.Setup(x => x.GetDirectoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerDirectoryDto("Players", "2 players", 2,
            [
                new PlayerDirectoryEntryDto(new PlayerSummaryDto(VicProfileId, "Vic A", "V", "", false), "3 matches", 3),
                new PlayerDirectoryEntryDto(new PlayerSummaryDto(Guid.NewGuid(), "Tobi K", "TK", "", false), "5 matches", 5),
            ]));

        var model = new AdminMatchPageModel(gameDay.Object, players.Object, new Mock<IAdminMatchNavigator>().Object);
        model.ApplyQueryAttributes(new Dictionary<string, object> { ["sessionId"] = SessionId.ToString() });
        return model;
    }
}
