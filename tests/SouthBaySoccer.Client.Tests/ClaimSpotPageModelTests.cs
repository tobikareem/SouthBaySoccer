using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public class ClaimSpotPageModelTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid ParticipantId = Guid.NewGuid();

    [Fact]
    public async Task Appearing_ForASession_ShowsUnclaimedEntriesAndTheCallersName()
    {
        var client = new Mock<IGameDayClient>();
        client.Setup(x => x.GetSessionClaimablesAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionClaimablesDto(SessionId, "Vic", AlreadyOnRoster: false,
                [new ClaimableParticipantDto(ParticipantId, "victor", IsWaitlist: true)]));
        var model = CreateModel(client, sessionId: SessionId);

        await model.AppearingCommand.ExecuteAsync(null);

        model.State.Should().Be(ViewState.Content);
        model.ShowEntries.Should().BeTrue();
        model.Prompt.Should().Contain("Vic");
        model.Entries.Should().ContainSingle(e => e.DisplayName == "victor");
    }

    [Fact]
    public async Task Claim_OnSuccess_NavigatesBack()
    {
        var client = new Mock<IGameDayClient>();
        client.Setup(x => x.GetSessionClaimablesAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionClaimablesDto(SessionId, "Vic", false,
                [new ClaimableParticipantDto(ParticipantId, "victor", true)]));
        client.Setup(x => x.ClaimParticipantAsync(SessionId, ParticipantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Success);
        var nav = new Mock<IClaimSpotNavigator>();
        var model = CreateModel(client, nav, SessionId);
        await model.AppearingCommand.ExecuteAsync(null);

        await model.ClaimCommand.ExecuteAsync(model.Entries[0]);

        client.Verify(x => x.ClaimParticipantAsync(SessionId, ParticipantId, It.IsAny<CancellationToken>()), Times.Once);
        nav.Verify(x => x.GoBackAsync(), Times.Once);
    }

    [Fact]
    public async Task Appearing_Onboarding_ListsRecentClaimableSessions()
    {
        var client = new Mock<IGameDayClient>();
        client.Setup(x => x.GetMyClaimableSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClaimableSessionDto(SessionId, "Bay Area Soccer", "Wed Jul 22, 7:30 PM", 3)]);
        var model = CreateModel(client);

        await model.AppearingCommand.ExecuteAsync(null);

        model.State.Should().Be(ViewState.Content);
        model.ShowSessions.Should().BeTrue();
        model.Sessions.Should().ContainSingle(s => s.Title == "Bay Area Soccer");
    }

    [Fact]
    public async Task Appearing_WhenNothingToClaim_ShowsEmpty()
    {
        var client = new Mock<IGameDayClient>();
        client.Setup(x => x.GetMyClaimableSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var model = CreateModel(client);

        await model.AppearingCommand.ExecuteAsync(null);

        model.State.Should().Be(ViewState.Empty);
    }

    private static ClaimSpotPageModel CreateModel(
        Mock<IGameDayClient> client,
        Mock<IClaimSpotNavigator>? navigator = null,
        Guid? sessionId = null)
    {
        var model = new ClaimSpotPageModel(client.Object, (navigator ?? new Mock<IClaimSpotNavigator>()).Object);
        var query = new Dictionary<string, object>();
        if (sessionId is { } id)
        {
            query["sessionId"] = id.ToString();
        }

        model.ApplyQueryAttributes(query);
        return model;
    }
}
