using System.Net.Http;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

public class SchedulePageModelTests
{
    // FixedTimeProvider pins "today" to Sun 2026-07-05 UTC, so the Sunday-start weeks are
    // Jul 5-11 (this week), Jul 12-18 (next week), and Jul 19+ falls into month groups.
    private static readonly DateTime ThisWeekStart = new(2026, 7, 6, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NextWeekStart = new(2026, 7, 14, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LaterInJuly = new(2026, 7, 25, 16, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NextYear = new(2027, 1, 9, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Appearing_SessionsAcrossWeeks_GroupsByWeekThenMonth()
    {
        var dashboard = Dashboard(
            Featured(Summary("Marina", ThisWeekStart)),
            Summary("Stanford", NextWeekStart),
            Summary("Mitchell", LaterInJuly),
            Summary("Winter cup", NextYear));
        var pageModel = CreatePageModel(ClientReturning(dashboard).Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Groups.Select(group => group.Title).Should().Equal(
            SchedulePageModel.ThisWeekTitle,
            SchedulePageModel.NextWeekTitle,
            "July",
            "January 2027");
        pageModel.Groups[0].Sessions.Single().Title.Should().Be("Marina");
        pageModel.Groups[1].Sessions.Single().Title.Should().Be("Stanford");
        pageModel.Groups[2].Sessions.Single().Title.Should().Be("Mitchell");
        pageModel.Groups[3].Sessions.Single().Title.Should().Be("Winter cup");
    }

    [Fact]
    public async Task Appearing_SessionsOutOfOrder_OrdersByStartTimeWithinGroups()
    {
        var later = Summary("Later", ThisWeekStart.AddDays(3));
        var earlier = Summary("Earlier", ThisWeekStart);
        var dashboard = Dashboard(Featured(later), earlier);
        var pageModel = CreatePageModel(ClientReturning(dashboard).Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.Groups.Single().Sessions.Select(session => session.Title)
            .Should().Equal("Earlier", "Later");
    }

    [Fact]
    public async Task Appearing_CancelledSessionInDashboard_StaysVisibleInItsGroup()
    {
        var cancelled = Summary("Cuesta Park", NextWeekStart) with { IsCanceled = true, StatusLabel = "Cancelled" };
        var dashboard = Dashboard(Featured(Summary("Marina", ThisWeekStart)), cancelled);
        var pageModel = CreatePageModel(ClientReturning(dashboard).Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        var nextWeek = pageModel.Groups.Single(group => group.Title == SchedulePageModel.NextWeekTitle);
        nextWeek.Sessions.Single().IsCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task Appearing_NoSessions_ShowsEmptyState()
    {
        var pageModel = CreatePageModel(ClientReturning(Dashboard(featured: null)).Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.StateTitle.Should().Be(SchedulePageModel.EmptyTitle);
        pageModel.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task Appearing_NetworkFailure_ShowsOfflineState()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var pageModel = CreatePageModel(sessionsClient.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Offline);
        pageModel.StateTitle.Should().Be(SchedulePageModel.OfflineTitle);
    }

    [Fact]
    public async Task Appearing_SeedDashboard_ProducesContentFromFixtures()
    {
        var pageModel = CreatePageModel(new SeedSessionsClient(new SeedState()));

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Groups.SelectMany(group => group.Sessions)
            .Select(session => session.Title)
            .Should().Contain("Marina Field · Saturday pickup");
    }

    [Fact]
    public async Task ViewSessionDetail_DelegatesToNavigator()
    {
        var sessionId = Guid.NewGuid();
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        navigator.Setup(n => n.GoToSessionAsync(sessionId)).Returns(Task.CompletedTask);
        var pageModel = CreatePageModel(Mock.Of<ISessionsClient>(), navigator.Object);

        await pageModel.ViewSessionDetailCommand.ExecuteAsync(sessionId);

        navigator.Verify(n => n.GoToSessionAsync(sessionId), Times.Once);
    }

    [Fact]
    public async Task JoinWaitlist_FullSession_RefreshesScheduleWithActualWaitlistState()
    {
        var state = new SeedState();
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.JoinWaitlistAsync(
                SeedFixtures.StanfordSessionId,
                It.IsAny<CancellationToken>()))
            .Returns((Guid sessionId, CancellationToken _) =>
                Task.FromResult(state.JoinWaitlist(sessionId)));
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(state.GetDashboard()));
        var pageModel = CreatePageModel(sessionsClient.Object);

        await pageModel.JoinWaitlistCommand.ExecuteAsync(SeedFixtures.StanfordSessionId);

        var session = pageModel.Groups
            .SelectMany(group => group.Sessions)
            .Single(item => item.Id == SeedFixtures.StanfordSessionId);
        session.WaitlistCount.Should().Be(4);
        session.IsWaitlisted.Should().BeTrue();
        session.CanJoinWaitlist.Should().BeFalse();
        session.StatusLabel.Should().Be("You're waitlisted");
    }

    [Fact]
    public async Task JoinWaitlist_WhenClientRejects_ShowsActionableError()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.JoinWaitlistAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Failure("rsvp_closed", "RSVP is closed."));
        var pageModel = CreatePageModel(sessionsClient.Object);

        await pageModel.JoinWaitlistCommand.ExecuteAsync(Guid.NewGuid());

        pageModel.State.Should().Be(ViewState.Error);
        pageModel.StateTitle.Should().Be("Couldn't join the waitlist");
        pageModel.StateMessage.Should().Be("RSVP is closed.");
    }

    private static SchedulePageModel CreatePageModel(
        ISessionsClient sessionsClient,
        ISessionsNavigator? navigator = null) =>
        new(
            sessionsClient,
            navigator ?? Mock.Of<ISessionsNavigator>(),
            new ClientResponseCache(TimeProvider.System),
            new FixedTimeProvider());

    private static Mock<ISessionsClient> ClientReturning(SessionsDashboardDto dashboard)
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dashboard);
        return sessionsClient;
    }

    private static SessionsDashboardDto Dashboard(
        SessionSummaryDto? featured,
        params SessionSummaryDto[] comingUp) =>
        new(
            "Saturday crew",
            "Good morning, Tobi",
            "Paid",
            featured,
            null,
            "Coming up",
            "See schedule",
            comingUp);

    private static SessionSummaryDto Featured(SessionSummaryDto summary) => summary;

    private static SessionSummaryDto Summary(string title, DateTime startsAtUtc) =>
        new(
            Guid.NewGuid(),
            title,
            Venue: string.Empty,
            "7v7",
            startsAtUtc,
            DateLabel: "Jul 6",
            TimeLabel: "9:00 AM",
            StatusLabel: "Open",
            GoingCount: 0,
            Capacity: 20,
            IsFull: false,
            WaitlistCount: 0,
            RelativeLabel: null);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() =>
            new(2026, 7, 5, 9, 0, 0, TimeSpan.Zero);
    }
}
