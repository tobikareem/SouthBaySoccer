using System.Net.Http;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Contracts.Profiles;
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
        var pageModel = CreatePageModel(sessionsClient, navigator.Object, hour: 9);

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
        comingUp.CanJoinWaitlist.Should().BeTrue();
        pageModel.CanManageSessions.Should().BeTrue();
    }

    [Theory]
    [InlineData(9, "Good morning, Tobi")]
    [InlineData(13, "Good afternoon, Tobi")]
    [InlineData(19, "Good evening, Tobi")]
    public async Task Appearing_ProfileLoaded_BuildsGreetingFromLocalClockAndFirstName(
        int hour,
        string expectedGreeting)
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard with { Greeting = "Fallback greeting" });
        var profileClient = ProfileClientReturning("Tobi Kareem");
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = CreatePageModel(
            sessionsClient.Object,
            navigator.Object,
            profileClient.Object,
            hour);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.Greeting.Should().Be(expectedGreeting);
        profileClient.Verify(
            client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Appearing_ProfileCallCompletes_ClearsRefreshSpinner()
    {
        var profileGate = new TaskCompletionSource<PlayerProfileDto?>();
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard);
        var profileClient = new Mock<IProfileClient>();
        profileClient
            .Setup(client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()))
            .Returns(profileGate.Task);
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = CreatePageModel(
            sessionsClient.Object,
            navigator.Object,
            profileClient.Object);

        var load = pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.IsRefreshing.Should().BeTrue();
        profileGate.SetResult(new PlayerProfileDto(
            SeedFixtures.CurrentPlayerId,
            "Tobi Kareem",
            "Captain",
            "TK",
            new CareerStatsDto(0, 0, 0, 0, 0, 0),
            [],
            null));
        await load;

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.IsRefreshing.Should().BeFalse();
    }

    [Fact]
    public async Task Appearing_AfterProfileLoaded_UsesCachedProfileName()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard with { Greeting = "Fallback greeting" });
        var profileClient = ProfileClientReturning("Tobi Kareem");
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = CreatePageModel(
            sessionsClient.Object,
            navigator.Object,
            profileClient.Object,
            hour: 9);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.Greeting.Should().Be("Good morning, Tobi");
        sessionsClient.Verify(
            client => client.GetDashboardAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        profileClient.Verify(
            client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Refresh_AfterProfileLoaded_ForcesProfileReload()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard with { Greeting = "Fallback greeting" });
        var profileClient = ProfileClientReturning("Tobi Kareem");
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = CreatePageModel(
            sessionsClient.Object,
            navigator.Object,
            profileClient.Object,
            hour: 9);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.RefreshCommand.ExecuteAsync(null);

        profileClient.Verify(
            client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Appearing_ProfileUnavailable_UsesDashboardGreeting()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard with { Greeting = "Good morning, Captain" });
        var profileClient = new Mock<IProfileClient>();
        profileClient
            .Setup(client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerProfileDto?)null);
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = CreatePageModel(
            sessionsClient.Object,
            navigator.Object,
            profileClient.Object,
            hour: 19);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.Greeting.Should().Be("Good morning, Captain");
    }

    [Fact]
    public async Task Appearing_WithLinkedGroups_UsesPrimaryGroupNameAsHeaderLabel()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard with { GroupLabel = "N9ja Bay" });
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var groupsClient = GroupsClientReturning(
            new GroupChatDto(Guid.NewGuid(), "ext-1", "Weekend Warriors", 12, IsLinked: true, IsPrimary: false),
            new GroupChatDto(Guid.NewGuid(), "ext-2", "Sunday League", 20, IsLinked: true, IsPrimary: true));
        var pageModel = CreatePageModel(
            sessionsClient.Object,
            navigator.Object,
            groupsClient: groupsClient.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.GroupLabel.Should().Be("Sunday League", "the header shows the player's primary group chat");
    }

    [Fact]
    public async Task Appearing_WithNoLinkedGroups_FallsBackToDashboardGroupLabel()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard with { GroupLabel = "N9ja Bay" });
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = CreatePageModel(
            sessionsClient.Object,
            navigator.Object,
            groupsClient: GroupsClientReturning().Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.GroupLabel.Should().Be("N9ja Bay");
    }

    [Fact]
    public async Task CreateSession_Invoked_NavigatesToCreateSessionScreen()
    {
        var sessionsClient = new Mock<ISessionsClient>(MockBehavior.Strict);
        var navigator = new Mock<ISessionsNavigator>();
        navigator
            .Setup(item => item.GoToCreateSessionAsync())
            .Returns(Task.CompletedTask);
        var pageModel = CreatePageModel(sessionsClient.Object, navigator.Object);
        pageModel.CanManageSessions = true;

        await pageModel.CreateSessionCommand.ExecuteAsync(null);

        navigator.Verify(item => item.GoToCreateSessionAsync(), Times.Once);
    }

    [Fact]
    public async Task Appearing_RegularPlayerProfile_HidesCreateSessionAndBlocksNavigation()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard with { CanManageSessions = true });
        var profileClient = ProfileClientReturning("Tobi Kareem", role: "Player");
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = CreatePageModel(
            sessionsClient.Object,
            navigator.Object,
            profileClient.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.CreateSessionCommand.ExecuteAsync(null);

        pageModel.CanManageSessions.Should().BeFalse();
        pageModel.CreateSessionCommand.CanExecute(null).Should().BeFalse();
        navigator.Verify(item => item.GoToCreateSessionAsync(), Times.Never);
    }

    [Fact]
    public async Task Appearing_NoSessionsReturned_KeepsStatsEntryPointVisible()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyDashboard());
        var navigator = new Mock<ISessionsNavigator>(MockBehavior.Strict);
        var pageModel = CreatePageModel(sessionsClient.Object, navigator.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.StatsPromptTitle.Should().Be("Submit your latest stats");
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
        var pageModel = CreatePageModel(sessionsClient.Object, navigator.Object);

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
        var pageModel = CreatePageModel(sessionsClient.Object, navigator.Object);

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
        var pageModel = CreatePageModel(sessionsClient.Object, navigator.Object);

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
        var pageModel = CreatePageModel(sessionsClient.Object, navigator.Object);

        await pageModel.ViewSessionDetailCommand.ExecuteAsync(SeedFixtures.StanfordSessionId);

        navigator.Verify(item => item.GoToSessionAsync(SeedFixtures.StanfordSessionId), Times.Once);
    }

    [Fact]
    public async Task OpenStats_WhenLatestMatchAvailable_NavigatesToMatchStatsWithId()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard);
        var navigator = new Mock<ISessionsNavigator>();
        navigator
            .Setup(item => item.GoToMatchStatsAsync(SeedFixtures.FeaturedMatchId))
            .Returns(Task.CompletedTask);
        var pageModel = CreatePageModel(sessionsClient.Object, navigator.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.OpenStatsCommand.ExecuteAsync(null);

        navigator.Verify(item => item.GoToMatchStatsAsync(SeedFixtures.FeaturedMatchId), Times.Once);
        navigator.Verify(item => item.GoToStatsAsync(), Times.Never);
    }

    [Fact]
    public async Task OpenStats_WhenLatestMatchUnavailable_NavigatesToStatsTab()
    {
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard with { StatsPrompt = null });
        var navigator = new Mock<ISessionsNavigator>();
        navigator.Setup(item => item.GoToStatsAsync()).Returns(Task.CompletedTask);
        var pageModel = CreatePageModel(sessionsClient.Object, navigator.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        await pageModel.OpenStatsCommand.ExecuteAsync(null);

        pageModel.StatsPromptTitle.Should().Be("Submit your latest stats");
        navigator.Verify(item => item.GoToStatsAsync(), Times.Once);
        navigator.Verify(item => item.GoToMatchStatsAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task OpenStats_WhenPromptRequiresClaim_NavigatesToClaimSpotForTheSession()
    {
        var sessionId = Guid.NewGuid();
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard with
            {
                StatsPrompt = new StatsPromptDto(
                    Guid.Empty,
                    "Submit your latest stats",
                    "Find yourself on Wednesday pickup to add your goals and assists",
                    sessionId,
                    RequiresClaim: true),
            });
        var navigator = new Mock<ISessionsNavigator>();
        navigator.Setup(item => item.GoToClaimSpotAsync(sessionId)).Returns(Task.CompletedTask);
        var pageModel = CreatePageModel(sessionsClient.Object, navigator.Object);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.HasStatsPrompt.Should().BeTrue("the card shows even though this player isn't linked yet");
        pageModel.HasLatestMatch.Should().BeFalse();

        await pageModel.OpenStatsCommand.ExecuteAsync(null);

        navigator.Verify(item => item.GoToClaimSpotAsync(sessionId), Times.Once);
        navigator.Verify(item => item.GoToMatchStatsAsync(It.IsAny<Guid>()), Times.Never);
        navigator.Verify(item => item.GoToStatsAsync(), Times.Never);
    }

    [Fact]
    public async Task Appearing_WhenClaimPromptWasDismissed_HidesTheCard()
    {
        var sessionId = Guid.NewGuid();
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard with
            {
                StatsPrompt = new StatsPromptDto(
                    Guid.Empty,
                    "Submit your latest stats",
                    "Add your goals and assists",
                    sessionId,
                    RequiresClaim: true),
            });
        var dismissed = new Mock<IDismissedStatsPromptStore>();
        dismissed.Setup(store => store.IsDismissed(sessionId)).Returns(true);
        var pageModel = CreatePageModel(
            sessionsClient.Object,
            new Mock<ISessionsNavigator>().Object,
            dismissedPromptStore: dismissed.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.HasStatsPrompt.Should().BeFalse("the player already said none of the entries were them");
        pageModel.StatsPrompt.Should().BeNull();
    }

    [Fact]
    public async Task Appearing_WhenSubmitPromptWasDismissedForClaim_StillShowsRealSubmitCard()
    {
        // A real submit prompt (RequiresClaim = false) is never dismissable, so a dismissal recorded
        // against the same session must not suppress it once the player is linked.
        var sessionId = Guid.NewGuid();
        var sessionsClient = new Mock<ISessionsClient>();
        sessionsClient
            .Setup(client => client.GetDashboardAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Dashboard with
            {
                StatsPrompt = new StatsPromptDto(
                    Guid.NewGuid(),
                    "Submit your latest stats",
                    "Add your goals and assists",
                    sessionId,
                    RequiresClaim: false),
            });
        var dismissed = new Mock<IDismissedStatsPromptStore>();
        dismissed.Setup(store => store.IsDismissed(sessionId)).Returns(true);
        var pageModel = CreatePageModel(
            sessionsClient.Object,
            new Mock<ISessionsNavigator>().Object,
            dismissedPromptStore: dismissed.Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.HasStatsPrompt.Should().BeTrue();
        pageModel.HasLatestMatch.Should().BeTrue();
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
        var pageModel = CreatePageModel(sessionsClient.Object, navigator.Object);

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
        pageModel.ComingUpSessions.Single().IsWaitlisted.Should().BeTrue();
        pageModel.ComingUpSessions.Single().CanJoinWaitlist.Should().BeFalse();
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
        var pageModel = CreatePageModel(sessionsClient.Object, navigator.Object);

        await pageModel.JoinWaitlistCommand.ExecuteAsync(SeedFixtures.MarinaSessionId);

        sessionsClient.Verify(
            client => client.GetDashboardAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        pageModel.State.Should().Be(ViewState.Error);
        pageModel.StateTitle.Should().Be("Couldn't join the waitlist");
        pageModel.StateMessage.Should().Be("still space");
    }

    private static SessionsHomePageModel CreatePageModel(
        ISessionsClient sessionsClient,
        ISessionsNavigator navigator,
        IProfileClient? profileClient = null,
        int hour = 9,
        IDismissedStatsPromptStore? dismissedPromptStore = null,
        IGroupsClient? groupsClient = null) =>
        new(
            sessionsClient,
            navigator,
            profileClient ?? ProfileClientReturning("Tobi Kareem").Object,
            groupsClient ?? GroupsClientReturning().Object,
            dismissedPromptStore ?? new Mock<IDismissedStatsPromptStore>().Object,
            new FixedTimeProvider(hour));

    private static Mock<IGroupsClient> GroupsClientReturning(params GroupChatDto[] groups)
    {
        var groupsClient = new Mock<IGroupsClient>();
        groupsClient
            .Setup(client => client.GetMyGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupsResponse(groups.Length > 0, groups));

        return groupsClient;
    }

    private static Mock<IProfileClient> ProfileClientReturning(string displayName, string role = "GameAdmin")
    {
        var profileClient = new Mock<IProfileClient>();
        profileClient
            .Setup(client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerProfileDto(
                SeedFixtures.CurrentPlayerId,
                displayName,
                "Captain",
                "TK",
                new CareerStatsDto(0, 0, 0, 0, 0, 0),
                [],
                null,
                role));

        return profileClient;
    }

    private sealed class FixedTimeProvider(int hour) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() =>
            new(2026, 7, 5, hour, 0, 0, TimeSpan.Zero);
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
