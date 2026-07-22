using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Leaderboards;
using SouthBaySoccer.Contracts.Stats;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public sealed class ApiSprint03ClientTests
{
    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void AddSouthBaySoccerClients_ApiSelected_ResolvesAllSprint03ApiClients()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<ISecureTokenStore>());
        services.AddSingleton(TimeProvider.System);

        services.AddSouthBaySoccerClients(
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api },
            new PickupPalOptions());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISessionsClient>().Should().BeOfType<ApiSessionsClient>();
        provider.GetRequiredService<IRosterClient>().Should().BeOfType<ApiRosterClient>();
        provider.GetRequiredService<IStatsClient>().Should().BeOfType<ApiStatsClient>();
        provider.GetRequiredService<ILeaderboardClient>().Should().BeOfType<ApiLeaderboardClient>();
        provider.GetRequiredService<IGameDayClient>().Should().BeOfType<ApiGameDayClient>();
    }

    [Fact]
    public async Task ApiSessionsClient_GetDashboardAsync_MapsPublishedSessionsWithLocalLabels()
    {
        var requests = new List<HttpRequestMessage>();
        var client = CreateSessionsClient(request =>
        {
            requests.Add(request);
            return request.RequestUri!.PathAndQuery.Contains("/rsvp/me")
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : JsonResponse(SessionsJson);
        });

        var dashboard = await client.GetDashboardAsync(CancellationToken.None);

        requests[0].Method.Should().Be(HttpMethod.Get);
        requests[0].RequestUri!.PathAndQuery.Should().Be("/sessions");
        var featured = dashboard.FeaturedSession;
        featured.Should().NotBeNull();
        featured!.Id.Should().Be(SessionId);
        featured.Title.Should().Be("Marina Field - Saturday pickup");
        featured.StatusLabel.Should().Be("Open");
        featured.DateLabel.Should().Be("Jul 25");
        featured.TimeLabel.Should().Be("4:00 PM");
        featured.RelativeLabel.Should().Be("Next match · in 2 days");
        dashboard.DuesStatus.Should().BeEmpty("no membership-status endpoint exists yet");
        dashboard.StatsPrompt.Should().BeNull("no stats-prompt endpoint exists yet");
        dashboard.ComingUpSessions.Should().BeEmpty("the draft session must be filtered out");
    }

    [Fact]
    public async Task ApiSessionsClient_GetDashboardAsync_WhenCallerIsGoing_MarksFeaturedSession()
    {
        var client = CreateSessionsClient(request =>
            request.RequestUri!.PathAndQuery.Contains("/rsvp/me")
                ? JsonResponse(GoingRsvpJson)
                : JsonResponse(SessionsJson));

        var dashboard = await client.GetDashboardAsync(CancellationToken.None);

        dashboard.FeaturedSession!.StatusLabel.Should().Be("You're going");
    }

    [Fact]
    public async Task ApiSessionsClient_GetSessionAsync_WhenCallerIsGoing_SetsIsGoing()
    {
        var client = CreateSessionsClient(request =>
            request.RequestUri!.PathAndQuery.Contains("/rsvp/me")
                ? JsonResponse(GoingRsvpJson)
                : JsonResponse(SessionsJson));

        var detail = await client.GetSessionAsync(SessionId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.IsGoing.Should().BeTrue();
        detail.IsRsvpAvailable.Should().BeTrue();
        detail.DeadlineLabel.Should().Be("closes 2d 3h");
        detail.DateTimeLabel.Should().Be("Sat Jul 25 · 4:00 PM");
    }

    [Fact]
    public async Task ApiRosterClient_SetRsvpIntentAsync_SendsGoingRsvpWithIdempotencyKey()
    {
        HttpRequestMessage? observed = null;
        var client = new ApiRosterClient(CreateHttpClient(request =>
        {
            observed = request;
            return JsonResponse(GoingRsvpJson);
        }));

        var result = await client.SetRsvpIntentAsync(SessionId, isGoing: true, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        observed!.Method.Should().Be(HttpMethod.Post);
        observed.RequestUri!.PathAndQuery.Should().Be($"/sessions/{SessionId}/rsvp");
        observed.Headers.Contains("Idempotency-Key").Should().BeTrue();
    }

    [Fact]
    public async Task ApiRosterClient_SetRsvpIntentAsync_ReusesIdempotencyKeyUntilSuccess()
    {
        var keys = new List<string>();
        var attempts = 0;
        var client = new ApiRosterClient(CreateHttpClient(request =>
        {
            keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : JsonResponse(GoingRsvpJson);
        }));

        var firstAttempt = async () => await client.SetRsvpIntentAsync(SessionId, isGoing: true, CancellationToken.None);
        await firstAttempt.Should().ThrowAsync<HttpRequestException>();
        (await client.SetRsvpIntentAsync(SessionId, isGoing: true, CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        await client.SetRsvpIntentAsync(SessionId, isGoing: true, CancellationToken.None);

        keys[1].Should().Be(keys[0], "a retry after a failed attempt must replay the same key");
        keys[2].Should().NotBe(keys[0], "a new operation after success must use a fresh key");
    }

    [Fact]
    public async Task ApiRosterClient_SetRsvpIntentAsync_WhenServerWaitlists_PreservesWaitlistState()
    {
        var requests = new List<HttpRequestMessage>();
        var client = new ApiRosterClient(CreateHttpClient(request =>
        {
            requests.Add(request);
            return JsonResponse(WaitlistedRsvpJson);
        }));

        var result = await client.SetRsvpIntentAsync(SessionId, isGoing: true, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        requests.Should().ContainSingle();
        requests[0].Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task ApiRosterClient_GetRosterAsync_WhenCallerIsGoing_ComposesSelfOnlyRoster()
    {
        var client = new ApiRosterClient(CreateHttpClient(request =>
            request.RequestUri!.PathAndQuery.Contains("/rsvp/me")
                ? JsonResponse(GoingRsvpJson)
                : JsonResponse(MyProfileJson)));

        var roster = await client.GetRosterAsync(SessionId, CancellationToken.None);

        roster.Should().NotBeNull();
        roster!.Going.Should().HaveCount(1);
        roster.Going[0].IsCurrentPlayer.Should().BeTrue();
        roster.Going[0].Player.DisplayName.Should().Be("Tobi Kareem");
        roster.Going[0].Player.Initials.Should().Be("TK");
        roster.Waitlist.Should().BeEmpty();
    }

    [Fact]
    public async Task ApiRosterClient_GetRosterAsync_WhenNoRsvp_ReturnsEmptyRoster()
    {
        var client = new ApiRosterClient(CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.NoContent)));

        var roster = await client.GetRosterAsync(SessionId, CancellationToken.None);

        roster.Should().NotBeNull();
        roster!.Going.Should().BeEmpty();
        roster.Waitlist.Should().BeEmpty();
    }

    [Fact]
    public async Task ApiLeaderboardClient_GetRankingAsync_SendsLeaderboardRouteWithMetric()
    {
        HttpRequestMessage? observed = null;
        var seasonId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var client = new ApiLeaderboardClient(CreateHttpClient(request =>
        {
            observed = request;
            return JsonResponse(
                $$"""
                {
                  "seasonId": "{{seasonId}}",
                  "seasonLabel": "2026",
                  "metric": 0,
                  "note": "Approved stats only",
                  "rows": []
                }
                """);
        }));

        var leaderboard = await client.GetRankingAsync(seasonId, LeaderboardMetric.Goals, CancellationToken.None);

        leaderboard.SeasonId.Should().Be(seasonId);
        observed!.Method.Should().Be(HttpMethod.Get);
        observed.RequestUri!.PathAndQuery.Should()
            .Be($"/stats/leaderboards?seasonId={seasonId:D}&metric=Goals&page=1&pageSize=25");
    }

    [Fact]
    public async Task ApiLeaderboardClient_GetRankingAsync_WhenSeasonUnknown_ReturnsEmptyLeaderboard()
    {
        var seasonId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var client = new ApiLeaderboardClient(CreatePipelineClient(_ =>
            ProblemResponse(HttpStatusCode.BadRequest, "Query parameter 'seasonId' is invalid.")));

        var leaderboard = await client.GetRankingAsync(seasonId, LeaderboardMetric.Goals, CancellationToken.None);

        leaderboard.SeasonId.Should().Be(seasonId);
        leaderboard.Metric.Should().Be(LeaderboardMetric.Goals);
        leaderboard.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ApiLeaderboardClient_GetRankingAsync_WhenBadRequestIsNotSeasonRelated_Throws()
    {
        var client = new ApiLeaderboardClient(CreatePipelineClient(_ =>
            ProblemResponse(HttpStatusCode.BadRequest, "Metric is invalid.")));

        var act = async () => await client.GetRankingAsync(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            LeaderboardMetric.Goals,
            CancellationToken.None);

        await act.Should().ThrowAsync<ApiRequestException>();
    }

    [Fact]
    public async Task ApiStatsClient_SubmitRatingsAsync_SendsFeedbackRouteWithIdempotencyKey()
    {
        HttpRequestMessage? observed = null;
        var matchId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var playerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var client = new ApiStatsClient(CreateHttpClient(request =>
        {
            observed = request;
            return JsonResponse(
                $$"""
                {
                  "matchId": "{{matchId}}",
                  "affectedCount": 1
                }
                """);
        }));

        var result = await client.SubmitRatingsAsync(
            matchId,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            [new TeammateRatingDto(playerId, 9, IsLiked: true, IsMvp: true)],
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        observed!.Method.Should().Be(HttpMethod.Post);
        observed.RequestUri!.PathAndQuery.Should().Be($"/stats/matches/{matchId}/feedback");
        observed.Headers.Contains("Idempotency-Key").Should().BeTrue();
    }

    [Fact]
    public async Task ApiGameDayClient_CheckInAsync_SendsCurrentPlayerCheckInWithIdempotencyKey()
    {
        var requests = new List<HttpRequestMessage>();
        var idempotencyKey = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var client = new ApiGameDayClient(CreateHttpClient(request =>
        {
            requests.Add(request);
            return request.RequestUri!.PathAndQuery == "/profiles/me"
                ? JsonResponse(MyProfileJson)
                : JsonResponse(
                    """
                    {
                      "checkInId": "77777777-7777-7777-7777-777777777777",
                      "sessionId": "11111111-1111-1111-1111-111111111111",
                      "playerProfileId": "22222222-2222-2222-2222-222222222222",
                      "checkedInByPlayerProfileId": "22222222-2222-2222-2222-222222222222",
                      "checkedInAtUtc": "2026-07-25T15:50:00Z",
                      "outcome": "CheckedIn",
                      "isLateOverride": false,
                      "adminOverrideId": null,
                      "lateOverrideReason": null
                    }
                    """);
        }));

        ClientCommandResult result = await client.CheckInAsync(
            SessionId,
            idempotencyKey,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        requests.Should().HaveCount(2);
        requests[0].Method.Should().Be(HttpMethod.Get);
        requests[0].RequestUri!.PathAndQuery.Should().Be("/profiles/me");
        requests[1].Method.Should().Be(HttpMethod.Post);
        requests[1].RequestUri!.PathAndQuery.Should().Be($"/sessions/{SessionId}/check-ins");
        requests[1].Headers.GetValues("Idempotency-Key").Single()
            .Should().Be(idempotencyKey.ToString("N"));
    }

    private const string SessionsJson =
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
            "status": "Published"
          },
          {
            "sessionId": "44444444-4444-4444-4444-444444444444",
            "seasonId": "22222222-2222-2222-2222-222222222222",
            "venueId": "33333333-3333-3333-3333-333333333333",
            "recurrenceRuleId": null,
            "title": "Draft session",
            "format": "5v5",
            "capacity": 10,
            "teamCount": 2,
            "startsAtUtc": "2026-07-26T16:00:00Z",
            "checkInOpensAtUtc": "2026-07-26T15:45:00Z",
            "checkInClosesAtUtc": "2026-07-26T16:05:00Z",
            "rsvpDeadlineUtc": "2026-07-26T15:00:00Z",
            "occurrenceKey": null,
            "status": "Draft"
          }
        ]
        """;

    private const string GoingRsvpJson =
        """
        {
          "sessionId": "11111111-1111-1111-1111-111111111111",
          "playerProfileId": "22222222-2222-2222-2222-222222222222",
          "state": "Going",
          "rsvpResponseId": "33333333-3333-3333-3333-333333333333",
          "waitlistEntryId": null,
          "waitlistPosition": null,
          "promotedPlayerProfileId": null
        }
        """;

    private const string WaitlistedRsvpJson =
        """
        {
          "sessionId": "11111111-1111-1111-1111-111111111111",
          "playerProfileId": "22222222-2222-2222-2222-222222222222",
          "state": "Waitlisted",
          "rsvpResponseId": null,
          "waitlistEntryId": "55555555-5555-5555-5555-555555555555",
          "waitlistPosition": 4,
          "promotedPlayerProfileId": null
        }
        """;

    private const string CanceledRsvpJson =
        """
        {
          "sessionId": "11111111-1111-1111-1111-111111111111",
          "playerProfileId": "22222222-2222-2222-2222-222222222222",
          "state": "Canceled",
          "rsvpResponseId": null,
          "waitlistEntryId": null,
          "waitlistPosition": null,
          "promotedPlayerProfileId": null
        }
        """;

    private const string MyProfileJson =
        """
        {
          "playerProfileId": "22222222-2222-2222-2222-222222222222",
          "identityUserId": "66666666-6666-6666-6666-666666666666",
          "displayName": "Tobi Kareem",
          "preferredPosition": "Midfielder",
          "photoUri": null,
          "isGuest": false,
          "role": "GameAdmin",
          "emergencyContact": null
        }
        """;

    private static ApiSessionsClient CreateSessionsClient(
        Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(CreateHttpClient(send), new FixedTimeProvider());

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new StubHttpMessageHandler(send))
        {
            BaseAddress = new Uri("https://api.test/"),
        };

    private static HttpClient CreatePipelineClient(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new ApiExceptionHandler { InnerHandler = new StubHttpMessageHandler(send) })
        {
            BaseAddress = new Uri("https://api.test/"),
        };

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage ProblemResponse(HttpStatusCode statusCode, string detail) =>
        new(statusCode)
        {
            Content = new StringContent(
                $$"""{"title":"Validation failed","detail":"{{detail}}","status":{{(int)statusCode}}}""",
                System.Text.Encoding.UTF8,
                "application/problem+json"),
        };

    // Fixed instant + UTC local zone keep date labels deterministic across machines.
    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
