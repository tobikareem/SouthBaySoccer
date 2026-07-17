using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.WebUtilities;
using SouthBaySoccer.Application.Features.Stats;
using SouthBaySoccer.Contracts.Leaderboards;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Contracts.Stats;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Stats;

public sealed class StatsFunctions(
    CreateMatchCommandHandler createMatchHandler,
    RecordMatchEventsCommandHandler recordEventsHandler,
    RecordMatchResultsCommandHandler recordResultsHandler,
    ReviewMatchEventCommandHandler reviewMatchEventHandler,
    ResolveMatchReviewCommandHandler resolveMatchReviewHandler,
    SubmitPeerFeedbackCommandHandler submitFeedbackHandler,
    AddStatCorrectionCommandHandler addCorrectionHandler,
    ReassignProfileStatsCommandHandler reassignProfileStatsHandler,
    LockMatchStatsCommandHandler lockMatchStatsHandler,
    GetSeasonLeaderboardQueryHandler getSeasonLeaderboardHandler,
    GetPlayerStatsQueryHandler getPlayerStatsHandler,
    GetMyPlayerStatsQueryHandler getMyPlayerStatsHandler,
    GetPlayerRecentFormQueryHandler getPlayerRecentFormHandler,
    IdempotentRequestExecutor idempotentRequestExecutor)
{
    [Function(nameof(CreateMatch))]
    [RequirePolicy(AuthenticationPolicies.CanAssignTeams)]
    public async Task<HttpResponseData> CreateMatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stats/matches")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<CreateMatchRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(CreateMatch),
            GetIdempotencyKey(request),
            body,
            async token =>
            {
                var result = await createMatchHandler.HandleAsync(
                    new CreateMatchCommand(
                        body.SessionId,
                        body.MatchNumber,
                        body.Teams.Select(x => new CreateMatchTeamInput(x.MatchTeamId, x.TeamNumber, x.Name, x.CaptainPlayerProfileId)).ToArray(),
                        body.Assignments.Select(x => new TeamAssignmentInput(x.MatchTeamId, x.PlayerProfileId, x.Started, x.MinutesPlayed, x.PlayedGoalkeeper, x.Position)).ToArray()),
                    token);
                return new IdempotentResponse<MatchResponse>(HttpStatusCode.Created, ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(RecordMatchEvents))]
    [RequirePolicy(AuthenticationPolicies.CanRecordStats)]
    public async Task<HttpResponseData> RecordMatchEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "stats/matches/{matchId:guid}/events")] HttpRequestData request,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<RecordMatchEventsRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(RecordMatchEvents),
            GetIdempotencyKey(request),
            new { matchId, body.Events },
            async token =>
            {
                var result = await recordEventsHandler.HandleAsync(
                    new RecordMatchEventsCommand(
                        matchId,
                        body.Events.Select(x => new MatchEventInput(x.PlayerProfileId, x.AssistPlayerProfileId, x.MatchTeamId, ParseEnum<MatchEventType>(x.EventType, nameof(x.EventType)), x.Minute)).ToArray()),
                    token);
                return new IdempotentResponse<StatMutationResponse>(HttpStatusCode.OK, ToResponse(result));
            },
            cancellationToken);
    }


    [Function(nameof(ReviewMatchEvent))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> ReviewMatchEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stats/matches/{matchId:guid}/events/{matchEventId:guid}/review")] HttpRequestData request,
        Guid matchId,
        Guid matchEventId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<ReviewMatchEventRequest>(request, cancellationToken);
        var result = await reviewMatchEventHandler.HandleAsync(
            new ReviewMatchEventCommand(matchId, matchEventId, body.Approved, body.Note),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(ResolveMatchReview))]
    [RequirePolicy(AuthenticationPolicies.CanRecordStats)]
    public async Task<HttpResponseData> ResolveMatchReview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stats/matches/{matchId:guid}/review-resolution")] HttpRequestData request,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<ResolveMatchReviewRequest>(request, cancellationToken);
        var result = await resolveMatchReviewHandler.HandleAsync(
            new ResolveMatchReviewCommand(matchId, body.ResolutionNote, body.BeforeJson, body.AfterJson),
            cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }
    [Function(nameof(RecordMatchResults))]
    [RequirePolicy(AuthenticationPolicies.CanRecordStats)]
    public async Task<HttpResponseData> RecordMatchResults(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "stats/matches/{matchId:guid}/results")] HttpRequestData request,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<RecordMatchResultsRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(RecordMatchResults),
            GetIdempotencyKey(request),
            new { matchId, body.Results },
            async token =>
            {
                var result = await recordResultsHandler.HandleAsync(
                    new RecordMatchResultsCommand(
                        matchId,
                        body.Results.Select(x => new MatchResultInput(x.MatchTeamId, x.Wins, x.Draws, x.Losses, x.GoalsFor, x.GoalsAgainst)).ToArray()),
                    token);
                return new IdempotentResponse<StatMutationResponse>(HttpStatusCode.OK, ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(SubmitPeerFeedback))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> SubmitPeerFeedback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stats/matches/{matchId:guid}/feedback")] HttpRequestData request,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<SubmitPeerFeedbackRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(SubmitPeerFeedback),
            GetIdempotencyKey(request),
            new { matchId, body.Ratings, body.LikedPlayerProfileIds, body.MvpPlayerProfileId },
            async token =>
            {
                var result = await submitFeedbackHandler.HandleAsync(
                    new SubmitPeerFeedbackCommand(
                        matchId,
                        body.Ratings.Select(x => new PlayerRatingInput(x.RatedPlayerProfileId, x.Score)).ToArray(),
                        body.LikedPlayerProfileIds,
                        body.MvpPlayerProfileId),
                    token);
                return new IdempotentResponse<StatMutationResponse>(HttpStatusCode.OK, ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(LockMatchStats))]
    [RequirePolicy(AuthenticationPolicies.CanRecordStats)]
    public async Task<HttpResponseData> LockMatchStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stats/matches/{matchId:guid}/lock")] HttpRequestData request,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(LockMatchStats),
            GetIdempotencyKey(request),
            new { matchId },
            async token =>
            {
                var result = await lockMatchStatsHandler.HandleAsync(new LockMatchStatsCommand(matchId), token);
                return new IdempotentResponse<StatMutationResponse>(HttpStatusCode.OK, ToResponse(result));
            },
            cancellationToken);
    }
    [Function(nameof(AddStatCorrection))]
    [RequirePolicy(AuthenticationPolicies.CanRecordStats)]
    public async Task<HttpResponseData> AddStatCorrection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stats/matches/{matchId:guid}/corrections")] HttpRequestData request,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<AddStatCorrectionRequest>(request, cancellationToken);
        var result = await addCorrectionHandler.HandleAsync(
            new AddStatCorrectionCommand(matchId, body.PlayerProfileId, body.Reason, body.BeforeJson, body.AfterJson),
            cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.Created, ToResponse(result), cancellationToken);
    }

    [Function(nameof(ReassignProfileStats))]
    [RequirePolicy(AuthenticationPolicies.CanManagePlayers)]
    public async Task<HttpResponseData> ReassignProfileStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stats/profile-merge/reassign")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<ReassignProfileStatsRequest>(request, cancellationToken);
        var result = await reassignProfileStatsHandler.HandleAsync(
            new ReassignProfileStatsCommand(body.SourceGuestPlayerProfileId, body.TargetPlayerProfileId),
            cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(GetSeasonLeaderboard))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetSeasonLeaderboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "stats/leaderboards")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var query = QueryHelpers.ParseQuery(request.Url.Query);
        var result = await getSeasonLeaderboardHandler.HandleAsync(
            new GetSeasonLeaderboardQuery(
                ReadRequiredGuid(query, "seasonId"),
                ToDomainMetric(ReadMetric(query)),
                ReadOptionalInt(query, "page", 1),
                ReadOptionalInt(query, "pageSize", 25)),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(GetPlayerStats))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetPlayerStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "players/{playerProfileId:guid}/stats")] HttpRequestData request,
        Guid playerProfileId,
        CancellationToken cancellationToken)
    {
        var query = QueryHelpers.ParseQuery(request.Url.Query);
        var result = await getPlayerStatsHandler.HandleAsync(
            new GetPlayerStatsQuery(playerProfileId, ReadOptionalGuid(query, "seasonId")),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(GetMyPlayerStats))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetMyPlayerStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "players/me/stats")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var query = QueryHelpers.ParseQuery(request.Url.Query);
        var result = await getMyPlayerStatsHandler.HandleAsync(
            new GetMyPlayerStatsQuery(ReadOptionalGuid(query, "seasonId")),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(GetPlayerRecentForm))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetPlayerRecentForm(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "players/{playerProfileId:guid}/recent-form")] HttpRequestData request,
        Guid playerProfileId,
        CancellationToken cancellationToken)
    {
        var query = QueryHelpers.ParseQuery(request.Url.Query);
        var result = await getPlayerRecentFormHandler.HandleAsync(
            new GetPlayerRecentFormQuery(playerProfileId, ReadOptionalInt(query, "take", 5)),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }
    private static LeaderboardDto ToResponse(LeaderboardModel result) =>
        new(
            result.SeasonId,
            result.SeasonLabel,
            ToContractMetric(result.Metric),
            result.Note,
            result.Rows.Select(row => new LeaderboardRowDto(
                row.Rank,
                new SouthBaySoccer.Contracts.Players.PlayerSummaryDto(
                    row.Player.Id,
                    row.Player.DisplayName,
                    row.Player.Initials,
                    row.Player.Position,
                    row.Player.IsGuest),
                row.Appearances,
                row.Value)).ToArray());

    private static PlayerStatsResponse ToResponse(PlayerStatsModel result) =>
        new(
            result.PlayerProfileId,
            result.SeasonId,
            new CareerStatsDto(
                result.Matches,
                result.Goals,
                result.Assists,
                result.AverageRating,
                result.MvpAwards,
                result.Likes));

    private static PlayerRecentFormResponse ToResponse(RecentFormModel result) =>
        new(result.PlayerProfileId, result.Results.Select(ToContractRecentForm).ToArray());
    private static MatchResponse ToResponse(CreateMatchResultModel result) =>
        new(
            result.MatchId,
            result.SessionId,
            result.MatchNumber,
            result.Status,
            result.Teams.Select(x => new MatchTeamResponse(x.MatchTeamId, x.TeamNumber, x.Name, x.CaptainPlayerProfileId)).ToArray(),
            result.Assignments.Select(x => new TeamAssignmentResponse(x.MatchTeamId, x.PlayerProfileId, x.Started, x.MinutesPlayed, x.PlayedGoalkeeper, x.Position)).ToArray());

    private static StatMutationResponse ToResponse(StatMutationResult result) =>
        new(result.MatchId, result.AffectedCount);

    private static Guid ReadRequiredGuid(IDictionary<string, Microsoft.Extensions.Primitives.StringValues> query, string key)
    {
        if (query.TryGetValue(key, out var values) && Guid.TryParse(values.FirstOrDefault(), out var value))
        {
            return value;
        }

        throw new ValidationProblemException(new Dictionary<string, string[]>
        {
            [key] = ["A valid GUID is required."],
        });
    }

    private static Guid? ReadOptionalGuid(IDictionary<string, Microsoft.Extensions.Primitives.StringValues> query, string key)
    {
        if (!query.TryGetValue(key, out var values) || string.IsNullOrWhiteSpace(values.FirstOrDefault()))
        {
            return null;
        }

        if (Guid.TryParse(values.FirstOrDefault(), out var value))
        {
            return value;
        }

        throw new ValidationProblemException(new Dictionary<string, string[]>
        {
            [key] = ["A valid GUID is required."],
        });
    }

    private static int ReadOptionalInt(IDictionary<string, Microsoft.Extensions.Primitives.StringValues> query, string key, int defaultValue)
    {
        if (!query.TryGetValue(key, out var values) || string.IsNullOrWhiteSpace(values.FirstOrDefault()))
        {
            return defaultValue;
        }

        if (int.TryParse(values.FirstOrDefault(), out var value))
        {
            return value;
        }

        throw new ValidationProblemException(new Dictionary<string, string[]>
        {
            [key] = ["A valid integer is required."],
        });
    }

    private static LeaderboardMetric ReadMetric(IDictionary<string, Microsoft.Extensions.Primitives.StringValues> query)
    {
        if (!query.TryGetValue("metric", out var values) || string.IsNullOrWhiteSpace(values.FirstOrDefault()))
        {
            return LeaderboardMetric.Goals;
        }

        return ParseEnum<LeaderboardMetric>(values.First()!, "metric");
    }

    private static StatLeaderboardMetric ToDomainMetric(LeaderboardMetric metric) => metric switch
    {
        LeaderboardMetric.Goals => StatLeaderboardMetric.Goals,
        LeaderboardMetric.Assists => StatLeaderboardMetric.Assists,
        LeaderboardMetric.Rating => StatLeaderboardMetric.Rating,
        LeaderboardMetric.Mvp => StatLeaderboardMetric.Mvp,
        _ => StatLeaderboardMetric.Goals,
    };

    private static LeaderboardMetric ToContractMetric(StatLeaderboardMetric metric) => metric switch
    {
        StatLeaderboardMetric.Goals => LeaderboardMetric.Goals,
        StatLeaderboardMetric.Assists => LeaderboardMetric.Assists,
        StatLeaderboardMetric.Rating => LeaderboardMetric.Rating,
        StatLeaderboardMetric.Mvp => LeaderboardMetric.Mvp,
        _ => LeaderboardMetric.Goals,
    };

    private static MatchResult ToContractRecentForm(RecentFormOutcome outcome) => outcome switch
    {
        RecentFormOutcome.Win => MatchResult.Win,
        RecentFormOutcome.Draw => MatchResult.Draw,
        RecentFormOutcome.Loss => MatchResult.Loss,
        _ => MatchResult.Draw,
    };
    private static TEnum ParseEnum<TEnum>(string value, string propertyName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ValidationProblemException(new Dictionary<string, string[]>
        {
            [propertyName] = [$"'{value}' is not supported."],
        });
    }

    private static string GetIdempotencyKey(HttpRequestData request)
    {
        if (request.Headers.TryGetValues("Idempotency-Key", out var values))
        {
            var value = values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new ValidationProblemException(new Dictionary<string, string[]>
        {
            ["Idempotency-Key"] = ["The Idempotency-Key header is required for this operation."],
        });
    }

    private static async Task<T> ReadRequiredJsonAsync<T>(HttpRequestData request, CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<T>(cancellationToken);
        if (body is null)
        {
            throw new ValidationProblemException(new Dictionary<string, string[]>
            {
                ["body"] = ["A request body is required."],
            });
        }

        return body;
    }

    private static async Task<HttpResponseData> WriteJsonAsync<T>(HttpRequestData request, HttpStatusCode statusCode, T value, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(value, cancellationToken: cancellationToken);
        return response;
    }
}
