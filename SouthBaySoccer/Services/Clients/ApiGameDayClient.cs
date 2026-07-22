using System.Net;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Contracts.Rsvps;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiGameDayClient(HttpClient httpClient) : IGameDayClient
{
    public Task<GameDayContextDto?> GetTodayContextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<GameDayContextDto?>(null);
    }

    public Task<ClientCommandResult> CheckInAsync(
        Guid sessionId,
        Guid idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync(async () =>
        {
            var profile = await GetCurrentProfileAsync(cancellationToken);
            if (profile is null)
            {
                return ClientCommandResult.Failure(
                    "profile_required",
                    "Create your player profile before checking in.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"sessions/{sessionId}/check-ins")
            {
                Content = JsonContent.Create(new CheckInPlayerRequest(
                    profile.PlayerProfileId,
                    "CheckedIn")),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString("N"));

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        });

    private async Task<MyProfileResponse?> GetCurrentProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("profiles/me", cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<MyProfileResponse>(
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<CaptainAssignmentDto?> GetCaptainAssignmentAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<CaptainAssignmentDto?>(null);
    }

    public Task<ClientCommandResult> AssignCaptainsAsync(
        Guid sessionId,
        int captainCount,
        IReadOnlyList<Guid> captainIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MissingGameDayProjection("Captain assignment needs a backend game-day projection."));
    }

    public Task<TeamDraftDto?> GetTeamDraftAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<TeamDraftDto?>(null);
    }

    public Task<ClientCommandResult> SaveTeamPicksAsync(
        Guid sessionId,
        Guid teamId,
        IReadOnlyList<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MissingGameDayProjection("Team draft picks need a backend game-day projection."));
    }

    public Task<PostGameApprovalDto?> GetPostGameApprovalAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<PostGameApprovalDto?>(null);
    }

    public Task<ClientCommandResult> ApproveStatAsync(
        Guid sessionId,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MissingGameDayProjection("Post-game approval needs backend submission ids."));
    }

    public Task<ClientCommandResult> SaveTeamResultAsync(
        Guid sessionId,
        TeamResultUpdateDto result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MissingGameDayProjection("Team result updates need backend match-team ids."));
    }

    public Task<ClientCommandResult> PublishPostGameAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MissingGameDayProjection("Post-game publish needs a backend game-day closeout route."));
    }

    private static ClientCommandResult MissingGameDayProjection(string message) =>
        ClientCommandResult.Failure("missing_contract", message);

    private static async Task<ClientCommandResult> ExecuteCommandAsync(
        Func<Task<ClientCommandResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ApiRequestException ex) when (IsClientError(ex.StatusCode))
        {
            return ClientCommandResult.Failure($"http_{(int)ex.StatusCode!.Value}", ex.UserMessage);
        }
        catch (HttpRequestException ex) when (IsClientError(ex.StatusCode))
        {
            return ClientCommandResult.Failure($"http_{(int)ex.StatusCode!.Value}", ex.Message);
        }
    }

    private static bool IsClientError(HttpStatusCode? statusCode) =>
        statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;
}
