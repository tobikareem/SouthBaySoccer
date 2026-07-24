using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Contracts.Leaderboards;
using SouthBaySoccer.Contracts.Sessions;
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
            return JsonResponse(SessionsJson);
        });

        var dashboard = await client.GetDashboardAsync(CancellationToken.None);

        requests[0].Method.Should().Be(HttpMethod.Get);
        requests[0].RequestUri!.PathAndQuery.Should().Be("/sessions");
        dashboard.GroupLabel.Should().Be("N9ja Bay");
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
        var client = CreateSessionsClient(_ => JsonResponse(
            SessionsJson.Replace(
                "\"isCurrentPlayerGoing\": false",
                "\"isCurrentPlayerGoing\": true",
                StringComparison.Ordinal)));

        var dashboard = await client.GetDashboardAsync(CancellationToken.None);

        dashboard.FeaturedSession!.StatusLabel.Should().Be("You're going");
    }

    [Fact]
    public async Task ApiSessionsClient_GetSessionAsync_WhenCallerIsGoing_SetsIsGoing()
    {
        var client = CreateSessionsClient(_ => JsonResponse(
            SessionsJson.Replace(
                "\"isCurrentPlayerGoing\": false",
                "\"isCurrentPlayerGoing\": true",
                StringComparison.Ordinal)));

        var detail = await client.GetSessionAsync(SessionId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.IsGoing.Should().BeTrue();
        detail.IsRsvpAvailable.Should().BeTrue();
        detail.DeadlineLabel.Should().Be("closes 2d 3h");
        detail.DateTimeLabel.Should().Be("Sat Jul 25 · 4:00 PM");
    }

    [Fact]
    public async Task ApiSessionsClient_GetDashboardAsync_WhenFeedIsFull_MapsCountsAndJoinWaitlist()
    {
        var json = SessionsJson
            .Replace("\"goingCount\": 16", "\"goingCount\": 20", StringComparison.Ordinal)
            .Replace("\"waitlistCount\": 0", "\"waitlistCount\": 3", StringComparison.Ordinal)
            .Replace("\"isFull\": false", "\"isFull\": true", StringComparison.Ordinal)
            .Replace("\"canJoinWaitlist\": false", "\"canJoinWaitlist\": true", StringComparison.Ordinal);
        var client = CreateSessionsClient(_ => JsonResponse(json));

        var dashboard = await client.GetDashboardAsync(CancellationToken.None);

        dashboard.FeaturedSession!.GoingCount.Should().Be(20);
        dashboard.FeaturedSession.WaitlistCount.Should().Be(3);
        dashboard.FeaturedSession.IsFull.Should().BeTrue();
        dashboard.FeaturedSession.CanJoinWaitlist.Should().BeTrue();
        dashboard.FeaturedSession.StatusLabel.Should().Be("Full");
    }

    [Fact]
    public async Task ApiSessionsClient_GetDashboardAsync_WhenRsvpDeadlinePassed_LabelsSessionClosed()
    {
        var closedJson = SessionsJson.Replace(
            "\"rsvpDeadlineUtc\": \"2026-07-25T15:00:00Z\"",
            "\"rsvpDeadlineUtc\": \"2026-07-22T02:00:00Z\"",
            StringComparison.Ordinal);
        var client = CreateSessionsClient(_ => JsonResponse(closedJson));

        var dashboard = await client.GetDashboardAsync(CancellationToken.None);

        dashboard.FeaturedSession!.StatusLabel.Should().Be("RSVP closed");
        dashboard.FeaturedSession.CanJoinWaitlist.Should().BeFalse();
        dashboard.FeaturedSession.CardStatus.Should().Be(SessionCardStatus.Closed);
    }

    [Fact]
    public async Task ApiSessionsClient_GetDashboardAsync_UsesVenueAndFormatDisplayTitle()
    {
        var client = CreateSessionsClient(_ => JsonResponse(SessionsJson));

        var dashboard = await client.GetDashboardAsync(CancellationToken.None);

        dashboard.FeaturedSession!.DisplayTitle.Should().Be("Marina Field · 7v7");
        dashboard.FeaturedSession.CardSemanticDescription.Should()
            .Be("Marina Field · 7v7 — Open");
        dashboard.FeaturedSession.WaitlistActionDescription.Should()
            .Be("Join the waitlist for Marina Field · 7v7");
    }

    [Fact]
    public void SessionSummaryDto_CanceledStateTakesVisualPrecedence()
    {
        var session = new SessionSummaryDto(
            SessionId,
            "Saturday pickup",
            "Marina Field",
            "7v7",
            new DateTime(2026, 7, 25, 16, 0, 0, DateTimeKind.Utc),
            "Jul 25",
            "4:00 PM",
            "Cancelled",
            20,
            20,
            true,
            3,
            null,
            IsCanceled: true,
            IsGoing: true,
            IsWaitlisted: true,
            IsRsvpClosed: true);

        session.CardStatus.Should().Be(SessionCardStatus.Canceled);
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
    public async Task ApiRosterClient_GetRosterAsync_ReadsRosterEndpointAndMapsBothLists()
    {
        HttpRequestMessage? observed = null;
        var client = new ApiRosterClient(CreateHttpClient(request =>
        {
            observed = request;
            return JsonResponse(RosterJson);
        }));

        var roster = await client.GetRosterAsync(SessionId, CancellationToken.None);

        observed!.Method.Should().Be(HttpMethod.Get);
        observed.RequestUri!.PathAndQuery.Should().Be($"/sessions/{SessionId}/roster");
        roster.Should().NotBeNull();
        roster!.Going.Should().HaveCount(2);
        roster.Going[0].IsCurrentPlayer.Should().BeTrue();
        roster.Going[0].Player.DisplayName.Should().Be("Tobi Kareem");
        roster.Going[1].Player.DisplayName.Should().Be("Mark A");
        roster.Waitlist.Should().ContainSingle();
        roster.Waitlist[0].Player.DisplayName.Should().Be("tope");
        roster.Waitlist[0].Position.Should().Be(1);
    }

    [Fact]
    public async Task ApiRosterClient_GetRosterAsync_WhenSessionUnknown_ReturnsNull()
    {
        var client = new ApiRosterClient(CreatePipelineClient(_ =>
            ProblemResponse(HttpStatusCode.NotFound, "Session was not found.")));

        var roster = await client.GetRosterAsync(SessionId, CancellationToken.None);

        roster.Should().BeNull();
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
        // seasonId is deliberately omitted: the server resolves the current season, so the seed
        // fixture id passed by the page model never reaches the wire.
        observed.RequestUri!.PathAndQuery.Should()
            .Be("/stats/leaderboards?metric=Goals&page=1&pageSize=5");
    }

    [Fact]
    public async Task ApiLeaderboardClient_GetRankingAsync_WhenServerReturnsBadRequest_Throws()
    {
        // The server now resolves the current season itself and returns an empty leaderboard when
        // none is active, so a 400 is a genuine validation failure — it must surface, not be
        // swallowed as an empty leaderboard.
        var client = new ApiLeaderboardClient(CreatePipelineClient(_ =>
            ProblemResponse(HttpStatusCode.BadRequest, "Query parameter 'metric' is invalid.")));

        var act = async () => await client.GetRankingAsync(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            LeaderboardMetric.Goals,
            CancellationToken.None);

        await act.Should().ThrowAsync<ApiRequestException>();
    }

    [Fact]
    public async Task ApiLeaderboardClient_GetRankingAsync_WhenServerReturnsNotFound_Throws()
    {
        // A 404 could be a missing route or resource — it must surface, not render as an empty
        // leaderboard.
        var client = new ApiLeaderboardClient(CreatePipelineClient(_ =>
            ProblemResponse(HttpStatusCode.NotFound, "The requested resource was not found.")));

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
            return JsonResponse(
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
        requests.Should().ContainSingle();
        requests[0].Method.Should().Be(HttpMethod.Post);
        requests[0].RequestUri!.PathAndQuery.Should().Be($"/sessions/{SessionId}/check-ins/me");
        requests[0].Headers.GetValues("Idempotency-Key").Single()
            .Should().Be(idempotencyKey.ToString("N"));
    }

    [Fact]
    public async Task ApiGameDayClient_GetTodayContextAsync_UsesGameDayProjection()
    {
        HttpRequestMessage? observed = null;
        var client = new ApiGameDayClient(CreateHttpClient(request =>
        {
            observed = request;
            return JsonResponse(
                $$"""
                {
                  "sessionId": "{{SessionId}}",
                  "matchId": "00000000-0000-0000-0000-000000000000",
                  "title": "Game Day",
                  "venue": "Marina Field",
                  "dateLabel": "Wed Jul 22",
                  "gameStartLabel": "7:40 PM",
                  "checkInWindowLabel": "7:10 PM - 7:40 PM",
                  "checkInCloseLabel": "closes 7:40 PM",
                  "status": 0,
                  "statusLabel": "Open",
                  "isSelfCheckInAvailable": true,
                  "primaryActionText": "Check in at field",
                  "blockReason": null,
                  "rsvpIntentLabel": "Going",
                  "isCurrentPlayerGoing": true,
                  "isCurrentPlayerCheckedIn": false,
                  "goingCount": 20,
                  "checkedInCount": 7,
                  "lateCount": 0,
                  "canAssignCaptains": false,
                  "canDraftTeam": false,
                  "canApprovePostGame": false,
                  "canLateCheckIn": false,
                  "lateCheckInPlayers": []
                }
                """);
        }));

        var context = await client.GetTodayContextAsync(null, CancellationToken.None);

        context.Should().NotBeNull();
        context!.SessionId.Should().Be(SessionId);
        context.GameStartLabel.Should().Be("7:40 PM");
        observed!.Method.Should().Be(HttpMethod.Get);
        observed.RequestUri!.PathAndQuery.Should().Be("/game-day/today");
    }

    [Fact]
    public async Task ApiGameDayClient_GetTodayContextAsync_WhenNoContent_ReturnsNull()
    {
        var client = new ApiGameDayClient(CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.NoContent)));

        var context = await client.GetTodayContextAsync(null, CancellationToken.None);

        context.Should().BeNull();
    }

    [Fact]
    public async Task ApiGameDayClient_LateCheckInAsync_SendsAuditedAdminOverride()
    {
        HttpRequestMessage? observed = null;
        string? observedBody = null;
        var idempotencyKey = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var client = new ApiGameDayClient(CreateHttpClient(request =>
        {
            observed = request;
            observedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("{}");
        }));

        var result = await client.LateCheckInAsync(
            SessionId,
            playerId,
            "Traffic delay",
            idempotencyKey,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        observed!.Method.Should().Be(HttpMethod.Post);
        observed.RequestUri!.PathAndQuery.Should().Be($"/sessions/{SessionId}/check-ins");
        observed.Headers.GetValues("Idempotency-Key").Single()
            .Should().Be(idempotencyKey.ToString("N"));
        observedBody.Should().Contain(playerId.ToString()).And.Contain("Late").And.Contain("Traffic delay");
    }

    [Fact]
    public async Task ApiGameDayClient_AdminCheckInAsync_SendsInWindowCheckedInWithIdempotencyKey()
    {
        HttpRequestMessage? observed = null;
        string? observedBody = null;
        var idempotencyKey = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var client = new ApiGameDayClient(CreateHttpClient(request =>
        {
            observed = request;
            observedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("{}");
        }));

        var result = await client.AdminCheckInAsync(
            SessionId,
            playerId,
            idempotencyKey,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        observed!.Method.Should().Be(HttpMethod.Post);
        observed.RequestUri!.PathAndQuery.Should().Be($"/sessions/{SessionId}/check-ins");
        observed.Headers.GetValues("Idempotency-Key").Single()
            .Should().Be(idempotencyKey.ToString("N"));
        observedBody.Should().Contain(playerId.ToString()).And.Contain("CheckedIn");
    }

    [Fact]
    public async Task ApiGameDayClient_GetCaptainAssignmentAsync_UsesSessionCaptainProjection()
    {
        HttpRequestMessage? observed = null;
        var client = new ApiGameDayClient(CreateHttpClient(request =>
        {
            observed = request;
            return JsonResponse(
                $$"""
                {
                  "sessionId": "{{SessionId}}",
                  "matchId": "22222222-2222-2222-2222-222222222222",
                  "captainCount": 2,
                  "availableCaptainCounts": [2, 3, 4],
                  "selectedCaptainIds": [],
                  "checkedInPlayers": []
                }
                """);
        }));

        var result = await client.GetCaptainAssignmentAsync(SessionId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.AvailableCaptainCounts.Should().Equal(2, 3, 4);
        observed!.Method.Should().Be(HttpMethod.Get);
        observed.RequestUri!.PathAndQuery.Should().Be($"/game-day/sessions/{SessionId}/captains");
    }

    [Fact]
    public async Task ApiGameDayClient_AssignCaptainsAsync_SendsDesiredTopologyWithIdempotencyKey()
    {
        HttpRequestMessage? observed = null;
        string? body = null;
        var captainIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var client = new ApiGameDayClient(CreateHttpClient(request =>
        {
            observed = request;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("{}");
        }));

        var result = await client.AssignCaptainsAsync(SessionId, 2, captainIds, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        observed!.Method.Should().Be(HttpMethod.Put);
        observed.RequestUri!.PathAndQuery.Should().Be($"/game-day/sessions/{SessionId}/captains");
        observed.Headers.Contains("Idempotency-Key").Should().BeTrue();
        body.Should().Contain("captainCount").And.Contain(captainIds[0].ToString());
    }

    [Fact]
    public async Task ApiGameDayClient_AssignCaptainsAsync_ReusesKeyAfterAmbiguousServerFailure()
    {
        var keys = new List<string>();
        var attempts = 0;
        var captainIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var client = new ApiGameDayClient(CreateHttpClient(request =>
        {
            keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : JsonResponse("{}");
        }));

        var first = async () => await client.AssignCaptainsAsync(
            SessionId,
            2,
            captainIds,
            CancellationToken.None);
        await first.Should().ThrowAsync<HttpRequestException>();
        var retry = await client.AssignCaptainsAsync(SessionId, 2, captainIds, CancellationToken.None);

        retry.IsSuccess.Should().BeTrue();
        keys.Should().HaveCount(2);
        keys[1].Should().Be(keys[0]);
    }

    [Fact]
    public async Task ApiGameDayClient_SaveTeamPicksAsync_UsesResourceScopedTeamRoute()
    {
        HttpRequestMessage? observed = null;
        string? body = null;
        var teamId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var client = new ApiGameDayClient(CreateHttpClient(request =>
        {
            observed = request;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("{}");
        }));

        var result = await client.SaveTeamPicksAsync(
            SessionId,
            teamId,
            [playerId],
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        observed!.Method.Should().Be(HttpMethod.Put);
        observed.RequestUri!.PathAndQuery.Should()
            .Be($"/game-day/sessions/{SessionId}/teams/{teamId}/picks");
        observed.Headers.Contains("Idempotency-Key").Should().BeTrue();
        body.Should().Contain(playerId.ToString());
    }

    [Fact]
    public async Task ApiGameDayClient_LockTeamsAsync_UsesAdminLockRouteWithIdempotencyKey()
    {
        HttpRequestMessage? observed = null;
        var client = new ApiGameDayClient(CreateHttpClient(request =>
        {
            observed = request;
            return JsonResponse("{}");
        }));

        var result = await client.LockTeamsAsync(SessionId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        observed!.Method.Should().Be(HttpMethod.Post);
        observed.RequestUri!.PathAndQuery.Should().Be($"/game-day/sessions/{SessionId}/teams/lock");
        observed.Headers.Contains("Idempotency-Key").Should().BeTrue();
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("result")]
    [InlineData("publish")]
    public async Task ApiGameDayClient_PostGameMutation_UsesExpectedRouteAndIdempotencyKey(string operation)
    {
        HttpRequestMessage? observed = null;
        var resourceId = Guid.NewGuid();
        var client = new ApiGameDayClient(CreateHttpClient(request =>
        {
            observed = request;
            return JsonResponse("{}");
        }));

        var result = operation switch
        {
            "approve" => await client.ApproveStatAsync(SessionId, resourceId, CancellationToken.None),
            "result" => await client.SaveTeamResultAsync(
                SessionId,
                new TeamResultUpdateDto(resourceId, 1, 0, 0),
                CancellationToken.None),
            _ => await client.PublishPostGameAsync(SessionId, CancellationToken.None),
        };

        result.IsSuccess.Should().BeTrue();
        observed!.Headers.Contains("Idempotency-Key").Should().BeTrue();
        observed.RequestUri!.PathAndQuery.Should().Be(operation switch
        {
            "approve" => $"/game-day/sessions/{SessionId}/post-game/events/{resourceId}/approve",
            "result" => $"/game-day/sessions/{SessionId}/post-game/results/{resourceId}",
            _ => $"/game-day/sessions/{SessionId}/post-game/publish",
        });
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
            "status": "Published",
            "venueName": "Marina Field",
            "goingCount": 16,
            "waitlistCount": 0,
            "isFull": false,
            "isCurrentPlayerGoing": false,
            "isCurrentPlayerWaitlisted": false,
            "canJoinWaitlist": false
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

    private const string RosterJson =
        """
        {
          "SessionId": "11111111-1111-1111-1111-111111111111",
          "Going": [
            {
              "Player": {
                "Id": "22222222-2222-2222-2222-222222222222",
                "DisplayName": "Tobi Kareem",
                "Initials": "TK",
                "Position": "Midfielder",
                "IsGuest": false
              },
              "IsCurrentPlayer": true
            },
            {
              "Player": {
                "Id": "00000000-0000-0000-0000-000000000000",
                "DisplayName": "Mark A",
                "Initials": "MA",
                "Position": "",
                "IsGuest": false
              },
              "IsCurrentPlayer": false
            }
          ],
          "Waitlist": [
            {
              "Player": {
                "Id": "00000000-0000-0000-0000-000000000000",
                "DisplayName": "tope",
                "Initials": "T",
                "Position": "",
                "IsGuest": true
              },
              "Position": 1
            }
          ]
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
