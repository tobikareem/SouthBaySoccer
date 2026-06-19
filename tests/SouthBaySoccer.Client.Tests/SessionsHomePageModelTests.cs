using System.Net.Http;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public class SessionsHomePageModelTests
{
    [Fact]
    public async Task Appearing_SeedDashboard_PopulatesContentFromWireframeFixtures()
    {
        var sessionsClient = new SeedSessionsClient(new SeedState());
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = new SessionsHomePageModel(sessionsClient, navigator.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.GroupLabel.Should().Be("Saturday crew");
        pageModel.Greeting.Should().Be("Good morning, Tobi");
        pageModel.DuesStatus.Should().Be("Paid");
        pageModel.FeaturedSession!.Title.Should().Be("Marina Field · Saturday pickup");
        pageModel.FeaturedSession.GoingCount.Should().Be(16);
        pageModel.FeaturedSession.Capacity.Should().Be(20);
        pageModel.StatsPrompt!.Title.Should().Be("Submit your latest stats");
        pageModel.ComingUpLabel.Should().Be("Coming up");
        pageModel.ScheduleActionLabel.Should().Be("See schedule");
        var comingUp = pageModel.ComingUpSessions.Single();
        comingUp.Title.Should().Be("Stanford Turf · 5v5");
        comingUp.IsFull.Should().BeTrue();
        comingUp.WaitlistCount.Should().Be(3);
    }

    [Fact]
    public async Task Appearing_NoSessionsReturned_ShowsEmptyState()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyDashboard());
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = new SessionsHomePageModel(sessionsClient.Object, navigator.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.StateTitle.Should().Be(SessionsHomePageModel.EmptyTitle);
        pageModel.ComingUpSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Appearing_ClientThrows_ShowsErrorStateWithoutThrowing()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = new SessionsHomePageModel(sessionsClient.Object, navigator.Object);

        var act = () => pageModel.AppearingCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        pageModel.State.Should().Be(ViewState.Error);
        pageModel.StateTitle.Should().Be(SessionsHomePageModel.ErrorTitle);
    }

    [Fact]
    public async Task Appearing_NetworkFailure_ShowsOfflineState()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = new SessionsHomePageModel(sessionsClient.Object, navigator.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Offline);
        pageModel.StateTitle.Should().Be(SessionsHomePageModel.OfflineTitle);
    }

    [Fact]
    public async Task Refresh_AfterError_RecoversToContent()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .SetupSequence(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"))
            .ReturnsAsync(SeedFixtures.Dashboard);
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = new SessionsHomePageModel(sessionsClient.Object, navigator.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.RefreshCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.FeaturedSession!.Title.Should().Be("Marina Field · Saturday pickup");
    }

    [Fact]
    public async Task ViewSessionDetail_Invoked_NavigatesToSessionWithId()
    {
        var sessionsClient = new Mock<ISessionsClient>(MockBehavior.Strict);
        var navigator = new Mock<ISessionsNavigator>();
        navigator
            .Setup(item => item.GoToSessionAsync(SeedFixtures.StanfordSessionId))
            .Returns(Task.CompletedTask);
        var pageModel = new SessionsHomePageModel(sessionsClient.Object, navigator.Object);

        await pageModel.ViewSessionDetailCommand.ExecuteAsync(SeedFixtures.StanfordSessionId);

        navigator.Verify(item => item.GoToSessionAsync(SeedFixtures.StanfordSessionId), Times.Once);
    }

    [Fact]
    public async Task OpenMatchStats_Invoked_NavigatesToMatchStatsWithId()
    {
        var sessionsClient = new Mock<ISessionsClient>(MockBehavior.Strict);
        var navigator = new Mock<ISessionsNavigator>();
        navigator
            .Setup(item => item.GoToMatchStatsAsync(SeedFixtures.FeaturedMatchId))
            .Returns(Task.CompletedTask);
        var pageModel = new SessionsHomePageModel(sessionsClient.Object, navigator.Object);

        await pageModel.OpenMatchStatsCommand.ExecuteAsync(SeedFixtures.FeaturedMatchId);

        navigator.Verify(item => item.GoToMatchStatsAsync(SeedFixtures.FeaturedMatchId), Times.Once);
    }

    [Fact]
    public async Task JoinWaitlist_FullSession_CallsClientAndRefreshesDashboard()
    {
        var state = new SeedState();
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.JoinWaitlistAsync(
                SeedFixtures.StanfordSessionId,
                It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken token) =>
                Task.FromResult(state.JoinWaitlist(id)));
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(state.GetDashboard()));
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = new SessionsHomePageModel(sessionsClient.Object, navigator.Object);

        await pageModel.JoinWaitlistCommand.ExecuteAsync(SeedFixtures.StanfordSessionId);

        sessionsClient.Verify(
            client => client.JoinWaitlistAsync(
                SeedFixtures.StanfordSessionId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        sessionsClient.Verify(
            client => client.GetDashboardAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        pageModel.State.Should().Be(ViewState.Content);
        pageModel.ComingUpSessions.Single().WaitlistCount.Should().Be(4);
    }

    [Fact]
    public async Task JoinWaitlist_ClientFailure_DoesNotRefreshDashboard()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.JoinWaitlistAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Failure("session_not_full", "still space"));
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = new SessionsHomePageModel(sessionsClient.Object, navigator.Object);

        await pageModel.JoinWaitlistCommand.ExecuteAsync(SeedFixtures.MarinaSessionId);

        sessionsClient.Verify(
            client => client.GetDashboardAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static SessionsDashboardDto EmptyDashboard() =>
        new(
            "Saturday crew",
            "Good morning, Tobi",
            "Paid",
            null!,
            null!,
            "Coming up",
            "See schedule",
            []);
}
