using System.Net.Http;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Contracts.Rosters;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.Client.Tests;

public class SessionDetailPageModelTests
{
    [Fact]
    public async Task Load_SeededMarinaSession_PopulatesDetailAndRosterAndShowsContent()
    {
        var pageModel = new SessionDetailPageModel(
            new SeedSessionsClient(new SeedState()),
            new SeedRosterClient(new SeedState()));

        ApplyQuery(pageModel, SeedFixtures.MarinaSessionId);
        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Venue.Should().Be("Marina Field");
        pageModel.Eyebrow.Should().Be("Saturday pickup");
        pageModel.GoingCount.Should().Be(16);
        pageModel.Capacity.Should().Be(20);
        pageModel.DeadlineLabel.Should().Be("closes 1d 4h");
        pageModel.Going.Should().HaveCount(16);
        pageModel.Going[0].Name.Should().Be("Tobi Kareem");
        pageModel.Going[0].IsViewer.Should().BeTrue();
        pageModel.IsGoing.Should().BeTrue();
        pageModel.CanRsvp.Should().BeTrue();
    }

    [Fact]
    public async Task Load_SeededMarinaSession_CollapsesGoingRosterToPreviewWithMoreAffordance()
    {
        var pageModel = new SessionDetailPageModel(
            new SeedSessionsClient(new SeedState()),
            new SeedRosterClient(new SeedState()));

        ApplyQuery(pageModel, SeedFixtures.MarinaSessionId);
        await pageModel.LoadCommand.ExecuteAsync(null);

        // The wireframe shows four going players above a "+ 12 more going" affordance, while the
        // section heading still reflects the full count.
        pageModel.GoingHeading.Should().Be("Going · 16");
        pageModel.GoingPreview.Should().HaveCount(SessionDetailPageModel.GoingPreviewLimit);
        pageModel.GoingPreview[0].Name.Should().Be("Tobi Kareem");
        pageModel.HasMoreGoing.Should().BeTrue();
        pageModel.MoreGoingCount.Should().Be(12);
        pageModel.MoreGoingLabel.Should().Be("+ 12 more going");
    }

    [Fact]
    public void SessionDetailPageXaml_CapacityLabel_UsesCurrentAndMaxGoing()
    {
        var xaml = ReadXaml("SessionDetailPage.xaml");

        xaml.Should().Contain("<MultiBinding StringFormat=\"{}{0} / {1} going\">");
        xaml.Should().Contain("<Binding Path=\"GoingCount\" />");
        xaml.Should().Contain("<Binding Path=\"Capacity\" />");
        xaml.Should().NotContain("StringFormat='{0} going'");
    }

    [Fact]
    public async Task Load_GoingRosterWithinPreviewLimit_ShowsAllGoingAndNoMoreAffordance()
    {
        var sessions = SessionsReturning(Detail(rsvpAvailable: true, isGoing: false));
        var roster = new Mock<IRosterClient>();
        roster
            .Setup(client => client.GetRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RosterWithGoing(3));
        var pageModel = new SessionDetailPageModel(sessions.Object, roster.Object);
        ApplyQuery(pageModel, SeedFixtures.MarinaSessionId);

        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.GoingPreview.Should().HaveCount(3);
        pageModel.GoingPreview.Should().BeSameAs(pageModel.Going);
        pageModel.HasMoreGoing.Should().BeFalse();
        pageModel.MoreGoingCount.Should().Be(0);
    }

    [Fact]
    public async Task Load_SeededMarinaSession_ShowsWaitlist()
    {
        var pageModel = new SessionDetailPageModel(
            new SeedSessionsClient(new SeedState()),
            new SeedRosterClient(new SeedState()));

        ApplyQuery(pageModel, SeedFixtures.MarinaSessionId);
        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.HasWaitlist.Should().BeTrue();
        pageModel.WaitlistHeading.Should().Be("Waitlist · 3");
        pageModel.Waitlist[0].Name.Should().Be("Tunde B.");
        pageModel.Waitlist[0].IsNextUp.Should().BeTrue();
    }

    [Fact]
    public async Task Load_SeededStanfordSession_OrdersWaitlistAndFlagsGuestNextUp()
    {
        var pageModel = new SessionDetailPageModel(
            new SeedSessionsClient(new SeedState()),
            new SeedRosterClient(new SeedState()));

        ApplyQuery(pageModel, SeedFixtures.StanfordSessionId);
        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.Waitlist.Should().HaveCount(3);
        var first = pageModel.Waitlist[0];
        first.Position.Should().Be(1);
        first.Name.Should().Be("Tunde B.");
        first.IsGuest.Should().BeTrue();
        first.IsNextUp.Should().BeTrue();
        pageModel.Waitlist.Select(entry => entry.Position).Should().BeInAscendingOrder();
        pageModel.CanRsvp.Should().BeFalse("a full session is not RSVP-available");
    }

    [Fact]
    public async Task Load_MissingSession_ShowsEmptyStateAndDoesNotThrow()
    {
        var pageModel = new SessionDetailPageModel(
            new SeedSessionsClient(new SeedState()),
            new SeedRosterClient(new SeedState()));

        ApplyQuery(pageModel, Guid.NewGuid());
        var act = () => pageModel.LoadCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.Going.Should().BeEmpty();
        pageModel.Waitlist.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_SessionsClientThrows_ShowsErrorStateAndDoesNotThrow()
    {
        var sessions = new Mock<ISessionsClient>();
        sessions
            .Setup(client => client.GetSessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var pageModel = new SessionDetailPageModel(
            sessions.Object, new Mock<IRosterClient>(MockBehavior.Strict).Object);

        ApplyQuery(pageModel, SeedFixtures.MarinaSessionId);
        var act = () => pageModel.LoadCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        pageModel.State.Should().Be(ViewState.Error);
    }

    [Fact]
    public async Task Load_NetworkFailure_ShowsOfflineState()
    {
        var sessions = new Mock<ISessionsClient>();
        sessions
            .Setup(client => client.GetSessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var pageModel = new SessionDetailPageModel(
            sessions.Object, new Mock<IRosterClient>(MockBehavior.Strict).Object);

        ApplyQuery(pageModel, SeedFixtures.MarinaSessionId);
        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Offline);
    }

    [Fact]
    public async Task ToggleRsvp_ViewerWasNotGoing_CallsClientWithIsGoingTrueAndUpdatesIntent()
    {
        var sessions = SessionsReturning(Detail(rsvpAvailable: true, isGoing: false));
        var roster = new Mock<IRosterClient>();
        roster
            .Setup(client => client.GetRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyRoster);
        roster
            .Setup(client => client.SetRsvpIntentAsync(
                SeedFixtures.MarinaSessionId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Success);
        var pageModel = new SessionDetailPageModel(sessions.Object, roster.Object);
        ApplyQuery(pageModel, SeedFixtures.MarinaSessionId);
        await pageModel.LoadCommand.ExecuteAsync(null);
        pageModel.IsGoing.Should().BeFalse();

        await pageModel.ToggleRsvpCommand.ExecuteAsync(null);

        pageModel.IsGoing.Should().BeTrue();
        roster.Verify(
            client => client.SetRsvpIntentAsync(
                SeedFixtures.MarinaSessionId, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ToggleRsvp_IntentRejected_RevertsOptimisticToggle()
    {
        var sessions = SessionsReturning(Detail(rsvpAvailable: true, isGoing: false));
        var roster = new Mock<IRosterClient>();
        roster
            .Setup(client => client.GetRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyRoster);
        roster
            .Setup(client => client.SetRsvpIntentAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Failure("session_full", "This session is full."));
        var pageModel = new SessionDetailPageModel(sessions.Object, roster.Object);
        ApplyQuery(pageModel, SeedFixtures.MarinaSessionId);
        await pageModel.LoadCommand.ExecuteAsync(null);

        await pageModel.ToggleRsvpCommand.ExecuteAsync(null);

        pageModel.IsGoing.Should().BeFalse();
        pageModel.State.Should().Be(ViewState.Content);
    }

    [Fact]
    public async Task ToggleRsvp_IntentThrows_RevertsOptimisticToggleAndDoesNotThrow()
    {
        var sessions = SessionsReturning(Detail(rsvpAvailable: true, isGoing: true));
        var roster = new Mock<IRosterClient>();
        roster
            .Setup(client => client.GetRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyRoster);
        roster
            .Setup(client => client.SetRsvpIntentAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var pageModel = new SessionDetailPageModel(sessions.Object, roster.Object);
        ApplyQuery(pageModel, SeedFixtures.MarinaSessionId);
        await pageModel.LoadCommand.ExecuteAsync(null);
        pageModel.IsGoing.Should().BeTrue();

        var act = () => pageModel.ToggleRsvpCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        pageModel.IsGoing.Should().BeTrue();
    }

    [Fact]
    public async Task ToggleRsvp_RsvpUnavailable_DoesNotCallClient()
    {
        var sessions = SessionsReturning(Detail(rsvpAvailable: false, isGoing: false));
        var roster = new Mock<IRosterClient>();
        roster
            .Setup(client => client.GetRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyRoster);
        var pageModel = new SessionDetailPageModel(sessions.Object, roster.Object);
        ApplyQuery(pageModel, SeedFixtures.MarinaSessionId);
        await pageModel.LoadCommand.ExecuteAsync(null);

        await pageModel.ToggleRsvpCommand.ExecuteAsync(null);

        pageModel.IsGoing.Should().BeFalse();
        roster.Verify(
            client => client.SetRsvpIntentAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static string ReadXaml(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Xaml", fileName);
        File.Exists(path).Should().BeTrue($"the test project must copy {fileName} to its output");
        return File.ReadAllText(path);
    }

    private static Mock<ISessionsClient> SessionsReturning(SessionDetailDto detail)
    {
        var sessions = new Mock<ISessionsClient>();
        sessions
            .Setup(client => client.GetSessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        return sessions;
    }

    private static SessionDetailDto Detail(bool rsvpAvailable, bool isGoing) =>
        new(
            SeedFixtures.MarinaSessionId,
            "Saturday pickup",
            "Marina Field",
            "Marina Field · 7v7",
            "7v7",
            new DateTime(2026, 6, 20, 16, 0, 0, DateTimeKind.Utc),
            "Sat Jun 20 · 9:00 AM",
            16,
            20,
            "closes 1d 4h",
            !rsvpAvailable,
            rsvpAvailable,
            isGoing);

    private static RosterDto EmptyRoster =>
        new(SeedFixtures.MarinaSessionId, [], []);

    private static RosterDto RosterWithGoing(int count) =>
        new(
            SeedFixtures.MarinaSessionId,
            [.. Enumerable.Range(0, count).Select(index => new RosterEntryDto(
                new PlayerSummaryDto(
                    Guid.NewGuid(), $"Player {index}", "P", "Midfielder", false, null),
                index == 0))],
            []);

    private static void ApplyQuery(SessionDetailPageModel pageModel, Guid sessionId) =>
        pageModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["sessionId"] = sessionId.ToString(),
        });
}
