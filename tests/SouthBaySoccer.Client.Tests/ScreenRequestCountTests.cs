using System.Net;
using System.Net.Http;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Client.Tests.TestSupport;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.Client.Tests;

/// <summary>
/// Phase 0.2 request-count harness from <c>_specs/perf/2026-07-performance-review.md</c>: pins the
/// exact HTTP requests each hot screen's page model issues on Appearing TODAY, over the real Api
/// clients. No client cache exists yet (finding C1), so screens refetch on every Appearing; the
/// only memoization anywhere is inside <see cref="SessionsHomePageModel"/>'s own fields (profile
/// name + group label) and <see cref="LeaderboardPageModel"/>'s one-time group load. Phase 5's
/// caching layer must update these expectations deliberately, in the same PR — any other diff
/// here is a regression.
/// </summary>
public sealed class ScreenRequestCountTests
{
    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GroupId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly TimeProvider Clock = new FixedTimeProvider();

    [Fact]
    public async Task SessionsHomeAppearing_FirstLoad_IssuesFeedPendingStatsProfileAndGroupsRequests()
    {
        var handler = CreateSessionsHomeHandler();
        var pageModel = CreateSessionsHomePageModel(handler);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(
            ViewState.Content,
            "the canned payloads must deserialize, or the request set below under-counts");
        handler.Requests.Should().Equal(
            (HttpMethod.Get, "/sessions"),
            (HttpMethod.Get, "/stats/submissions/pending"),
            (HttpMethod.Get, "/profiles/me"),
            (HttpMethod.Get, "/players/me/groups"));
    }

    [Fact]
    public async Task SessionsHomeAppearing_InvokedTwice_RefetchesFeedAndPendingStatsEveryLoad_OnlyProfileAndGroupsAreModelCached()
    {
        // Pins the no-cache status quo: every Appearing re-downloads the sessions feed and the
        // pending-stats prompt. The profile greeting and group label do NOT refetch — the page
        // model memoizes them in private fields until a pull-to-refresh — so a second Appearing
        // adds 2 requests, not 4. Phase 5 moves this ad-hoc memoization into the client cache.
        var handler = CreateSessionsHomeHandler();
        var pageModel = CreateSessionsHomePageModel(handler);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        handler.Requests.Should().Equal(
            (HttpMethod.Get, "/sessions"),
            (HttpMethod.Get, "/stats/submissions/pending"),
            (HttpMethod.Get, "/profiles/me"),
            (HttpMethod.Get, "/players/me/groups"),
            (HttpMethod.Get, "/sessions"),
            (HttpMethod.Get, "/stats/submissions/pending"));
        handler.Count("/sessions").Should().Be(2);
        handler.Count("/stats/submissions/pending").Should().Be(2);
        handler.Count("/profiles/me").Should().Be(1);
        handler.Count("/players/me/groups").Should().Be(1);
    }

    [Fact]
    public async Task ScheduleAppearing_FirstLoad_IssuesFeedAndPendingStatsRequests()
    {
        // The Schedule tab re-downloads the same dashboard pair the Home tab just fetched
        // (finding C1: Shell keeps tabs alive, so every tab switch repeats these 2 requests).
        var handler = new CountingHttpMessageHandler();
        handler.RegisterJson("/sessions", SessionsFeedJson);
        handler.RegisterStatus("/stats/submissions/pending", HttpStatusCode.NoContent);
        var pageModel = new SchedulePageModel(
            new ApiSessionsClient(CreateHttpClient(handler), Clock),
            new Mock<ISessionsNavigator>(MockBehavior.Strict).Object,
            Clock);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        handler.Requests.Should().Equal(
            (HttpMethod.Get, "/sessions"),
            (HttpMethod.Get, "/stats/submissions/pending"));
    }

    [Fact]
    public async Task PlayersAppearing_FirstLoad_IssuesSingleDirectoryRequest()
    {
        var handler = CreatePlayersHandler();
        var pageModel = CreatePlayersPageModel(handler);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        handler.Requests.Should().Equal(
            (HttpMethod.Get, "/players/directory"));
    }

    [Fact]
    public async Task PlayersAppearing_InvokedTwice_RefetchesDirectoryEveryScreenLoad_NoCacheToday()
    {
        // Pins the no-cache status quo: the full (unpaginated) directory downloads again on every
        // Appearing. Phase 5 turns the second load into 0 requests within the cache TTL.
        var handler = CreatePlayersHandler();
        var pageModel = CreatePlayersPageModel(handler);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        handler.Requests.Should().Equal(
            (HttpMethod.Get, "/players/directory"),
            (HttpMethod.Get, "/players/directory"));
    }

    [Fact]
    public async Task ProfileAppearing_FirstLoad_IssuesSingleProfileMeRequest()
    {
        var handler = new CountingHttpMessageHandler();
        handler.RegisterJson("/profiles/me", ProfileMeJson);
        var pageModel = new ProfilePageModel(
            new ApiProfileClient(CreateHttpClient(handler)),
            new Mock<IProfileExternalLauncher>(MockBehavior.Strict).Object,
            new Mock<IProfileNavigator>(MockBehavior.Strict).Object,
            new Mock<IAuthenticationCoordinator>(MockBehavior.Strict).Object,
            new Mock<IUserDialogService>(MockBehavior.Strict).Object);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        handler.Requests.Should().Equal(
            (HttpMethod.Get, "/profiles/me"));
    }

    [Fact]
    public async Task LeaderboardAppearing_FirstLoad_IssuesGroupsThenGroupScopedLeaderboardRequests()
    {
        // First Appearing loads the player's groups (once per page-model instance — an in-model
        // guard, not a cache) and then the top-5 ranking scoped to their primary group.
        var handler = new CountingHttpMessageHandler();
        handler.RegisterJson("/players/me/groups", MyGroupsJson);
        handler.RegisterJson("/stats/leaderboards", LeaderboardJson);
        var pageModel = new LeaderboardPageModel(
            new ApiLeaderboardClient(CreateHttpClient(handler)),
            new ApiGroupsClient(CreateHttpClient(handler)),
            new Mock<ILeaderboardNavigator>(MockBehavior.Strict).Object,
            new LeaderboardOptions());

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        handler.Requests.Should().Equal(
            (HttpMethod.Get, "/players/me/groups"),
            (HttpMethod.Get, $"/stats/leaderboards?metric=Goals&page=1&pageSize=5&groupId={GroupId}"));
    }

    [Fact]
    public async Task SessionDetailLoad_KnownSessionId_DownloadsEntireSessionsFeedThenRoster()
    {
        // Finding C3: ApiSessionsClient.GetSessionAsync has no single-session endpoint, so opening
        // one session downloads the ENTIRE feed and filters client-side, then fetches the roster.
        // Phase 5 resolves the detail from the cached feed instead of re-downloading it.
        var handler = new CountingHttpMessageHandler();
        handler.RegisterJson("/sessions", SessionsFeedJson);
        handler.RegisterJson($"/sessions/{SessionId}/roster", RosterJson);
        var httpClient = CreateHttpClient(handler);
        var pageModel = new SessionDetailPageModel(
            new ApiSessionsClient(httpClient, Clock),
            new ApiRosterClient(httpClient));
        pageModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["sessionId"] = SessionId.ToString(),
        });

        await pageModel.LoadCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        handler.Requests.Should().Equal(
            (HttpMethod.Get, "/sessions"),
            (HttpMethod.Get, $"/sessions/{SessionId}/roster"));
    }

    private static HttpClient CreateHttpClient(CountingHttpMessageHandler handler) =>
        new(handler)
        {
            BaseAddress = new Uri("https://api.test/"),
        };

    private static CountingHttpMessageHandler CreateSessionsHomeHandler()
    {
        var handler = new CountingHttpMessageHandler();
        handler.RegisterJson("/sessions", SessionsFeedJson);
        handler.RegisterStatus("/stats/submissions/pending", HttpStatusCode.NoContent);
        handler.RegisterJson("/profiles/me", ProfileMeJson);
        handler.RegisterJson("/players/me/groups", MyGroupsJson);
        return handler;
    }

    private static SessionsHomePageModel CreateSessionsHomePageModel(CountingHttpMessageHandler handler)
    {
        var httpClient = CreateHttpClient(handler);
        return new SessionsHomePageModel(
            new ApiSessionsClient(httpClient, Clock),
            new Mock<ISessionsNavigator>(MockBehavior.Strict).Object,
            new ApiProfileClient(httpClient),
            new ApiGroupsClient(httpClient),
            new Mock<IDismissedStatsPromptStore>().Object,
            Clock);
    }

    private static CountingHttpMessageHandler CreatePlayersHandler()
    {
        var handler = new CountingHttpMessageHandler();
        handler.RegisterJson("/players/directory", DirectoryJson);
        return handler;
    }

    private static PlayersPageModel CreatePlayersPageModel(CountingHttpMessageHandler handler) =>
        new(
            new ApiPlayersClient(CreateHttpClient(handler)),
            new Mock<IPlayersNavigator>(MockBehavior.Strict).Object);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() =>
            new(2026, 7, 5, 9, 0, 0, TimeSpan.Zero);
    }

    private const string SessionsFeedJson =
        """
        [
          {
            "sessionId": "11111111-1111-1111-1111-111111111111",
            "seasonId": "22222222-2222-2222-2222-222222222222",
            "venueId": "33333333-3333-3333-3333-333333333333",
            "recurrenceRuleId": null,
            "title": "Marina Field - Saturday pickup",
            "format": "7v7",
            "capacity": 20,
            "teamCount": 2,
            "startsAtUtc": "2026-07-25T16:00:00Z",
            "checkInOpensAtUtc": "2026-07-25T15:45:00Z",
            "checkInClosesAtUtc": "2026-07-25T16:05:00Z",
            "rsvpDeadlineUtc": "2026-07-25T15:00:00Z",
            "occurrenceKey": null,
            "status": "Published",
            "venueName": "Marina Field",
            "goingCount": 16,
            "waitlistCount": 0,
            "isFull": false,
            "isCurrentPlayerGoing": false,
            "isCurrentPlayerWaitlisted": false,
            "canJoinWaitlist": false
          }
        ]
        """;

    private const string ProfileMeJson =
        """
        {
          "playerProfileId": "44444444-4444-4444-4444-444444444444",
          "identityUserId": null,
          "displayName": "Tobi Kareem",
          "preferredPosition": "Midfielder",
          "photoUri": null,
          "isGuest": false,
          "role": "Player",
          "emergencyContact": null
        }
        """;

    private const string MyGroupsJson =
        """
        {
          "isLinked": true,
          "groups": [
            {
              "id": "55555555-5555-5555-5555-555555555555",
              "externalId": "grp-001",
              "groupName": "Saturday crew",
              "memberCount": 24,
              "isLinked": true,
              "isPrimary": true
            }
          ]
        }
        """;

    private const string DirectoryJson =
        """
        {
          "title": "Players",
          "subtitle": "Search the crew and open career stats.",
          "totalPlayers": 1,
          "players": [
            {
              "player": {
                "id": "66666666-6666-6666-6666-666666666666",
                "displayName": "Ada Johnson",
                "initials": "AJ",
                "position": "Midfielder",
                "isGuest": false
              },
              "subtitle": "Midfielder · #1",
              "matches": 12
            }
          ]
        }
        """;

    private const string LeaderboardJson =
        """
        {
          "seasonId": "77777777-7777-7777-7777-777777777777",
          "seasonLabel": "2026",
          "metric": 0,
          "note": "Approved stats only",
          "rows": []
        }
        """;

    private const string RosterJson =
        """
        {
          "sessionId": "11111111-1111-1111-1111-111111111111",
          "going": [],
          "waitlist": []
        }
        """;
}
