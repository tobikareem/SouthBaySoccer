using System.Globalization;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Sessions;

public sealed class GameDayFunctions(
    GameDayPickupPalRefreshService pickupPalRefreshService,
    GetTodayGameDayContextQueryHandler contextHandler,
    GetRecentGamesQueryHandler recentGamesHandler,
    GetCaptainAssignmentQueryHandler getCaptainAssignmentHandler,
    AssignSessionCaptainsCommandHandler assignCaptainsHandler,
    LockSessionTeamsCommandHandler lockSessionTeamsHandler,
    GetTeamDraftQueryHandler getTeamDraftHandler,
    SaveCaptainTeamPicksCommandHandler saveTeamPicksHandler,
    GetPostGameApprovalQueryHandler getPostGameApprovalHandler,
    ApprovePostGameStatCommandHandler approvePostGameStatHandler,
    SavePostGameTeamResultCommandHandler savePostGameTeamResultHandler,
    PublishPostGameCommandHandler publishPostGameHandler,
    ReopenPostGameResultsCommandHandler reopenPostGameResultsHandler,
    LinkParticipantToProfileCommandHandler linkParticipantHandler,
    GetSessionClaimablesQueryHandler getSessionClaimablesHandler,
    GetMyClaimableSessionsQueryHandler getMyClaimableSessionsHandler,
    ClaimParticipantCommandHandler claimParticipantHandler,
    IdempotentRequestExecutor idempotentRequestExecutor)
{
    [Function(nameof(GetTodayGameDayContext))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetTodayGameDayContext(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "game-day/today")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        await pickupPalRefreshService.RefreshIfStaleAsync(cancellationToken);
        var context = await contextHandler.HandleAsync(cancellationToken);
        if (context is null)
        {
            return request.CreateResponse(HttpStatusCode.NoContent);
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(ToResponse(context), cancellationToken);
        return response;
    }

    [Function(nameof(GetMyClaimableSessions))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetMyClaimableSessions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "game-day/claimable")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var sessions = await getMyClaimableSessionsHandler.HandleAsync(new GetMyClaimableSessionsQuery(), cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(
            sessions
                .Select(x => new ClaimableSessionDto(
                    x.SessionId,
                    x.Title,
                    ToLocal(x.StartsAtUtc).ToString("ddd MMM d, h:mm tt", CultureInfo.InvariantCulture),
                    x.ClaimableCount))
                .ToArray(),
            cancellationToken);
        return response;
    }

    [Function(nameof(GetSessionClaimables))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetSessionClaimables(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sessions/{sessionId:guid}/claimable")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var model = await getSessionClaimablesHandler.HandleAsync(new GetSessionClaimablesQuery(sessionId), cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(
            new SessionClaimablesDto(
                model.SessionId,
                model.MyRegisteredName,
                model.AlreadyOnRoster,
                model.Claimable
                    .Select(c => new ClaimableParticipantDto(c.ParticipantId, c.DisplayName, c.IsWaitlist))
                    .ToArray()),
            cancellationToken);
        return response;
    }

    [Function(nameof(ClaimParticipant))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> ClaimParticipant(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions/{sessionId:guid}/claim")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<ClaimParticipantRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(ClaimParticipant),
            GetIdempotencyKey(request),
            new { sessionId, body.ParticipantId },
            async token =>
            {
                var result = await claimParticipantHandler.HandleAsync(
                    new ClaimParticipantCommand(sessionId, body.ParticipantId),
                    token);
                return new IdempotentResponse<GameDayMutationResponse>(HttpStatusCode.OK, ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(GetRecentGames))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> GetRecentGames(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "game-day/recent")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var games = await recentGamesHandler.HandleAsync(cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(
            games
                .Select(game => new RecentGameDto(
                    game.SessionId,
                    game.MatchId,
                    game.Title,
                    game.Venue,
                    ToLocal(game.StartsAtUtc).ToString("ddd MMM d, h:mm tt", CultureInfo.InvariantCulture),
                    game.MatchStatus,
                    game.TeamCount,
                    game.PendingApprovalCount,
                    game.CanEditTeams))
                .ToArray(),
            cancellationToken);
        return response;
    }

    [Function(nameof(LockSessionTeams))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> LockSessionTeams(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "game-day/sessions/{sessionId:guid}/teams/lock")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(LockSessionTeams),
            GetIdempotencyKey(request),
            new { sessionId },
            async token =>
            {
                var result = await lockSessionTeamsHandler.HandleAsync(
                    new LockSessionTeamsCommand(sessionId),
                    token);
                return new IdempotentResponse<GameDayMutationResponse>(
                    HttpStatusCode.OK,
                    ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(GetCaptainAssignment))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> GetCaptainAssignment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "game-day/sessions/{sessionId:guid}/captains")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await getCaptainAssignmentHandler.HandleAsync(sessionId, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(AssignCaptains))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> AssignCaptains(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "game-day/sessions/{sessionId:guid}/captains")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<AssignCaptainsRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(AssignCaptains),
            GetIdempotencyKey(request),
            new { sessionId, body.CaptainCount, body.CaptainPlayerProfileIds },
            async token =>
            {
                var result = await assignCaptainsHandler.HandleAsync(
                    new AssignSessionCaptainsCommand(
                        sessionId,
                        body.CaptainCount,
                        body.CaptainPlayerProfileIds),
                    token);
                return new IdempotentResponse<GameDayMutationResponse>(
                    HttpStatusCode.OK,
                    ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(LinkParticipant))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> LinkParticipant(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "game-day/participants/{participantId:guid}/link")] HttpRequestData request,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<LinkParticipantRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(LinkParticipant),
            GetIdempotencyKey(request),
            new { participantId, body.PlayerProfileId },
            async token =>
            {
                var result = await linkParticipantHandler.HandleAsync(
                    new LinkParticipantToProfileCommand(participantId, body.PlayerProfileId),
                    token);
                return new IdempotentResponse<GameDayMutationResponse>(
                    HttpStatusCode.OK,
                    ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(GetTeamDraft))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetTeamDraft(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "game-day/sessions/{sessionId:guid}/draft")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await getTeamDraftHandler.HandleAsync(sessionId, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(SaveTeamPicks))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> SaveTeamPicks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "game-day/sessions/{sessionId:guid}/teams/{teamId:guid}/picks")] HttpRequestData request,
        Guid sessionId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<SaveTeamPicksRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(SaveTeamPicks),
            GetIdempotencyKey(request),
            new { sessionId, teamId, body.PlayerProfileIds },
            async token =>
            {
                var result = await saveTeamPicksHandler.HandleAsync(
                    new SaveCaptainTeamPicksCommand(sessionId, teamId, body.PlayerProfileIds),
                    token);
                return new IdempotentResponse<GameDayMutationResponse>(
                    HttpStatusCode.OK,
                    ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(GetPostGameApproval))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetPostGameApproval(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "game-day/sessions/{sessionId:guid}/post-game")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await getPostGameApprovalHandler.HandleAsync(sessionId, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(ApprovePostGameStat))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> ApprovePostGameStat(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "game-day/sessions/{sessionId:guid}/post-game/events/{matchEventId:guid}/approve")] HttpRequestData request,
        Guid sessionId,
        Guid matchEventId,
        CancellationToken cancellationToken)
    {
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(ApprovePostGameStat),
            GetIdempotencyKey(request),
            new { sessionId, matchEventId },
            async token =>
            {
                var result = await approvePostGameStatHandler.HandleAsync(
                    new ApprovePostGameStatCommand(sessionId, matchEventId),
                    token);
                return new IdempotentResponse<GameDayMutationResponse>(
                    HttpStatusCode.OK,
                    ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(SavePostGameTeamResult))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> SavePostGameTeamResult(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "game-day/sessions/{sessionId:guid}/post-game/results/{teamId:guid}")] HttpRequestData request,
        Guid sessionId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<SavePostGameTeamResultRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(SavePostGameTeamResult),
            GetIdempotencyKey(request),
            new { sessionId, teamId, body.Wins, body.Draws, body.Losses },
            async token =>
            {
                var result = await savePostGameTeamResultHandler.HandleAsync(
                    new SavePostGameTeamResultCommand(
                        sessionId,
                        teamId,
                        body.Wins,
                        body.Draws,
                        body.Losses),
                    token);
                return new IdempotentResponse<GameDayMutationResponse>(
                    HttpStatusCode.OK,
                    ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(ReopenPostGameResults))]
    [RequirePolicy(AuthenticationPolicies.CanManageSessions)]
    public async Task<HttpResponseData> ReopenPostGameResults(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "game-day/sessions/{sessionId:guid}/post-game/reopen")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(ReopenPostGameResults),
            GetIdempotencyKey(request),
            new { sessionId },
            async token =>
            {
                var result = await reopenPostGameResultsHandler.HandleAsync(
                    new ReopenPostGameResultsCommand(sessionId),
                    token);
                return new IdempotentResponse<GameDayMutationResponse>(
                    HttpStatusCode.OK,
                    ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(PublishPostGame))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> PublishPostGame(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "game-day/sessions/{sessionId:guid}/post-game/publish")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(PublishPostGame),
            GetIdempotencyKey(request),
            new { sessionId },
            async token =>
            {
                var result = await publishPostGameHandler.HandleAsync(
                    new PublishPostGameCommand(sessionId),
                    token);
                return new IdempotentResponse<GameDayMutationResponse>(
                    HttpStatusCode.OK,
                    ToResponse(result));
            },
            cancellationToken);
    }

    private static GameDayContextDto ToResponse(GameDayContextModel context)
    {
        var localStart = ToLocal(context.StartsAtUtc);
        var localOpen = ToLocal(context.CheckInOpensAtUtc);
        var localClose = ToLocal(context.CheckInClosesAtUtc);
        return new GameDayContextDto(
            context.SessionId,
            context.MatchId,
            "Game Day",
            context.Venue,
            localStart.ToString("ddd MMM d", CultureInfo.InvariantCulture),
            localStart.ToString("h:mm tt", CultureInfo.InvariantCulture),
            $"{localOpen:h:mm tt} - {localClose:h:mm tt}",
            $"closes {localClose:h:mm tt}",
            Enum.Parse<GameDayStatus>(context.Status),
            context.StatusLabel,
            context.IsSelfCheckInAvailable,
            context.PrimaryActionText,
            context.BlockReason,
            context.IsCurrentPlayerGoing ? "Going" : "Not going",
            context.IsCurrentPlayerGoing,
            context.IsCurrentPlayerCheckedIn,
            context.GoingCount,
            context.CheckedInCount,
            context.LateCount,
            context.CanAssignCaptains,
            context.CanDraftTeam,
            context.CanApprovePostGame,
            context.StartsAtUtc,
            context.CheckInOpensAtUtc,
            context.CheckInClosesAtUtc,
            context.CanLateCheckIn,
            context.LateCheckInPlayers
                .Select(player => new GameDayPlayerDto(
                    player.PlayerProfileId,
                    player.DisplayName,
                    player.IsGuest))
                .ToArray(),
            context.Roster
                .Select(entry => new GameDayRosterEntryDto(
                    entry.PlayerProfileId,
                    entry.DisplayName,
                    entry.IsGuest,
                    entry.IsWaitlist,
                    entry.IsCheckedIn))
                .ToArray(),
            context.CanManageCheckIns,
            context.CanSubmitOwnStats);
    }

    private static CaptainAssignmentDto ToResponse(CaptainAssignmentModel model) =>
        new(
            model.SessionId,
            model.MatchId,
            model.CaptainCount,
            model.AvailableCaptainCounts,
            model.SelectedCaptainIds,
            model.CheckedInPlayers.Select(ToResponse).ToArray(),
            model.CanLockTeams,
            model.IsLocked);

    private static TeamDraftDto ToResponse(TeamDraftModel model) =>
        new(
            model.SessionId,
            model.MatchId,
            model.TeamId,
            model.TeamName,
            model.CaptainName,
            model.CanPickPlayers,
            model.IsLocked,
            model.TeamCount,
            model.CheckedInPlayers.Select(ToResponse).ToArray(),
            model.Teams.Select(team => new MatchTeamDto(
                team.TeamId,
                team.Name,
                team.CaptainId,
                team.CaptainName,
                team.PlayerIds)).ToArray(),
            model.CanManageAllTeams);

    private static PostGameApprovalDto ToResponse(PostGameApprovalModel model) =>
        new(
            model.SessionId,
            model.MatchId,
            model.CanApprove,
            model.IsPublished,
            model.NeedsReview,
            model.TeamCount,
            model.TeamResults.Select(result => new TeamResultDto(
                result.TeamId,
                result.TeamName,
                result.Wins,
                result.Draws,
                result.Losses)).ToArray(),
            model.PendingApprovals.Select(approval => new PendingStatApprovalDto(
                approval.SubmissionId,
                ToResponse(approval.Player),
                approval.Goals,
                approval.Assists,
                Enum.Parse<StatApprovalStatus>(approval.Status),
                approval.AssistPlayer is null ? null : ToResponse(approval.AssistPlayer),
                approval.Detail)).ToArray(),
            model.CanReopenResults);

    private static CheckedInPlayerDto ToResponse(CheckedInGameDayPlayerModel player) =>
        new(ToResponse(player.Player), player.Detail);

    private static SouthBaySoccer.Contracts.Players.PlayerSummaryDto ToResponse(GameDayRosterPlayerModel player) =>
        new(player.Id, player.DisplayName, player.Initials, player.Position, player.IsGuest);

    private static GameDayMutationResponse ToResponse(GameDayMutationModel model) =>
        new(model.SessionId, model.MatchId, model.AffectedCount);

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

    private static async Task<T> ReadRequiredJsonAsync<T>(
        HttpRequestData request,
        CancellationToken cancellationToken)
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

    private static async Task<HttpResponseData> WriteJsonAsync<T>(
        HttpRequestData request,
        HttpStatusCode statusCode,
        T value,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(value, cancellationToken: cancellationToken);
        return response;
    }

    private static DateTime ToLocal(DateTime utc) => SessionAdminTimeZone.ToLocal(utc);
}
