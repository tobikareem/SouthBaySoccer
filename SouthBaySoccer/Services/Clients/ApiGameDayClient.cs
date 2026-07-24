using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Contracts.Rsvps;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiGameDayClient(HttpClient httpClient) : IGameDayClient
{
    private readonly ConcurrentDictionary<string, string> _idempotencyKeys = new();

    public async Task<GameDayContextDto?> GetTodayContextAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("game-day/today", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GameDayContextDto>(
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<RecentGameDto>> GetRecentGamesAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("game-day/recent", cancellationToken);
        // Non-admins simply have no recent-games list; that is not an error worth surfacing.
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NoContent)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<RecentGameDto>>(
                   cancellationToken: cancellationToken)
               ?? [];
    }

    public async Task<IReadOnlyList<ClaimableSessionDto>> GetMyClaimableSessionsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("game-day/claimable", cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NoContent)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ClaimableSessionDto>>(
                   cancellationToken: cancellationToken)
               ?? [];
    }

    public async Task<SessionClaimablesDto?> GetSessionClaimablesAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"sessions/{sessionId}/claimable", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionClaimablesDto>(cancellationToken: cancellationToken);
    }

    public Task<ClientCommandResult> ClaimParticipantAsync(Guid sessionId, Guid participantId, CancellationToken cancellationToken)
    {
        var operation = $"claim:{sessionId}:{participantId}";
        return ExecuteCommandAsync(async () =>
        {
            using var request = CreateIdempotentRequest(
                HttpMethod.Post,
                $"sessions/{sessionId}/claim",
                new ClaimParticipantRequest(participantId),
                GetIdempotencyKey(operation));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            CompleteDefinitiveResponse(operation, response.StatusCode);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        }, () => _idempotencyKeys.TryRemove(operation, out _));
    }

    public Task<ClientCommandResult> CheckInAsync(
        Guid sessionId,
        Guid idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"sessions/{sessionId}/check-ins/me");
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString("N"));

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        });

    public Task<ClientCommandResult> LateCheckInAsync(
        Guid sessionId,
        Guid playerProfileId,
        string reason,
        Guid idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"sessions/{sessionId}/check-ins")
            {
                Content = JsonContent.Create(new CheckInPlayerRequest(playerProfileId, "Late", reason)),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString("N"));

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        });

    public Task<ClientCommandResult> AdminCheckInAsync(
        Guid sessionId,
        Guid playerProfileId,
        Guid idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(async () =>
        {
            // In-window admin check-in for a confirmed player: the audited "Late" override path
            // (LateCheckInAsync) handles arrivals after the window closes.
            using var request = new HttpRequestMessage(HttpMethod.Post, $"sessions/{sessionId}/check-ins")
            {
                Content = JsonContent.Create(new CheckInPlayerRequest(playerProfileId, "CheckedIn")),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString("N"));

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        });

    public async Task<CaptainAssignmentDto?> GetCaptainAssignmentAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"game-day/sessions/{sessionId}/captains",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CaptainAssignmentDto>(
            cancellationToken: cancellationToken);
    }

    public Task<ClientCommandResult> AssignCaptainsAsync(
        Guid sessionId,
        int captainCount,
        IReadOnlyList<Guid> captainIds,
        CancellationToken cancellationToken)
    {
        var operation = $"captains:{sessionId}:{captainCount}:{string.Join(',', captainIds)}";
        return ExecuteCommandAsync(async () =>
        {
            using var request = CreateIdempotentRequest(
                HttpMethod.Put,
                $"game-day/sessions/{sessionId}/captains",
                new AssignCaptainsRequest(captainCount, captainIds),
                GetIdempotencyKey(operation));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            CompleteDefinitiveResponse(operation, response.StatusCode);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        }, () => _idempotencyKeys.TryRemove(operation, out _));
    }

    public async Task<TeamDraftDto?> GetTeamDraftAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"game-day/sessions/{sessionId}/draft",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TeamDraftDto>(
            cancellationToken: cancellationToken);
    }

    public Task<ClientCommandResult> SaveTeamPicksAsync(
        Guid sessionId,
        Guid teamId,
        IReadOnlyList<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        var canonicalPlayerIds = playerIds.Distinct().Order().ToArray();
        var operation = $"picks:{sessionId}:{teamId}:{Fingerprint(canonicalPlayerIds)}";
        return ExecuteCommandAsync(async () =>
        {
            using var request = CreateIdempotentRequest(
                HttpMethod.Put,
                $"game-day/sessions/{sessionId}/teams/{teamId}/picks",
                new SaveTeamPicksRequest(canonicalPlayerIds),
                GetIdempotencyKey(operation));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            CompleteDefinitiveResponse(operation, response.StatusCode);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        }, () => _idempotencyKeys.TryRemove(operation, out _));
    }

    public Task<ClientCommandResult> LockTeamsAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var operation = $"lock-teams:{sessionId}";
        return ExecuteCommandAsync(async () =>
        {
            using var request = CreateIdempotentRequest(
                HttpMethod.Post,
                $"game-day/sessions/{sessionId}/teams/lock",
                GetIdempotencyKey(operation));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            CompleteDefinitiveResponse(operation, response.StatusCode);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        }, () => _idempotencyKeys.TryRemove(operation, out _));
    }

    public async Task<PostGameApprovalDto?> GetPostGameApprovalAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"game-day/sessions/{sessionId}/post-game",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PostGameApprovalDto>(
            cancellationToken: cancellationToken);
    }

    public Task<ClientCommandResult> ApproveStatAsync(
        Guid sessionId,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var operation = $"approve:{sessionId}:{submissionId}";
        return ExecuteCommandAsync(async () =>
        {
            using var request = CreateIdempotentRequest(
                HttpMethod.Post,
                $"game-day/sessions/{sessionId}/post-game/events/{submissionId}/approve",
                GetIdempotencyKey(operation));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            CompleteDefinitiveResponse(operation, response.StatusCode);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        }, () => _idempotencyKeys.TryRemove(operation, out _));
    }

    public Task<ClientCommandResult> SaveTeamResultAsync(
        Guid sessionId,
        TeamResultUpdateDto result,
        CancellationToken cancellationToken)
    {
        var operation = $"result:{sessionId}:{result.TeamId}:{result.Wins}:{result.Draws}:{result.Losses}";
        return ExecuteCommandAsync(async () =>
        {
            using var request = CreateIdempotentRequest(
                HttpMethod.Put,
                $"game-day/sessions/{sessionId}/post-game/results/{result.TeamId}",
                new SavePostGameTeamResultRequest(result.Wins, result.Draws, result.Losses),
                GetIdempotencyKey(operation));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            CompleteDefinitiveResponse(operation, response.StatusCode);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        }, () => _idempotencyKeys.TryRemove(operation, out _));
    }

    public Task<ClientCommandResult> PublishPostGameAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var operation = $"publish:{sessionId}";
        return ExecuteCommandAsync(async () =>
        {
            using var request = CreateIdempotentRequest(
                HttpMethod.Post,
                $"game-day/sessions/{sessionId}/post-game/publish",
                GetIdempotencyKey(operation));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            CompleteDefinitiveResponse(operation, response.StatusCode);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        }, () => _idempotencyKeys.TryRemove(operation, out _));
    }

    private static HttpRequestMessage CreateIdempotentRequest(
        HttpMethod method,
        string requestUri,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static HttpRequestMessage CreateIdempotentRequest<T>(
        HttpMethod method,
        string requestUri,
        T body,
        string idempotencyKey)
    {
        var request = CreateIdempotentRequest(method, requestUri, idempotencyKey);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private string GetIdempotencyKey(string operation) =>
        _idempotencyKeys.GetOrAdd(operation, static _ => Guid.NewGuid().ToString("N"));

    private void CompleteDefinitiveResponse(string operation, HttpStatusCode statusCode)
    {
        if ((int)statusCode < 500)
        {
            _idempotencyKeys.TryRemove(operation, out _);
        }
    }

    private static string Fingerprint(IReadOnlyList<Guid> ids) =>
        string.Join(',', ids.Order());

    private static async Task<ClientCommandResult> ExecuteCommandAsync(
        Func<Task<ClientCommandResult>> operation,
        Action? onDefinitiveFailure = null)
    {
        try
        {
            return await operation();
        }
        catch (ApiRequestException ex) when (IsClientError(ex.StatusCode))
        {
            onDefinitiveFailure?.Invoke();
            return ClientCommandResult.Failure($"http_{(int)ex.StatusCode!.Value}", ex.UserMessage);
        }
        catch (HttpRequestException ex) when (IsClientError(ex.StatusCode))
        {
            onDefinitiveFailure?.Invoke();
            return ClientCommandResult.Failure($"http_{(int)ex.StatusCode!.Value}", ex.Message);
        }
    }

    private static bool IsClientError(HttpStatusCode? statusCode) =>
        statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;
}
