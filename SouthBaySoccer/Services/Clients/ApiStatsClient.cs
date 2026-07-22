using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Stats;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiStatsClient(HttpClient httpClient) : IStatsClient
{
    private readonly ConcurrentDictionary<Guid, string> _feedbackIdempotencyKeysByMatchId = new();

    public Task<MatchStatsDto?> GetMatchStatsAsync(Guid matchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<MatchStatsDto?>(null);
    }

    public Task<ClientCommandResult> SubmitStatsAsync(
        Guid matchId,
        int goals,
        int assists,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ClientCommandResult.Failure(
            "missing_contract",
            "Match-stat self submission needs a backend read/write projection for the current player."));
    }

    public Task<ClientCommandResult> ConfirmStatsAsync(
        Guid matchId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ClientCommandResult.Failure(
            "missing_contract",
            "Captain stat confirmation needs backend match-event submission ids."));
    }

    public Task<IReadOnlyList<RateableTeammateDto>> GetRateableTeammatesAsync(
        Guid matchId,
        Guid raterId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RateableTeammateDto>>(Array.Empty<RateableTeammateDto>());
    }

    public async Task<ClientCommandResult> SubmitRatingsAsync(
        Guid matchId,
        Guid raterId,
        IReadOnlyList<TeammateRatingDto> ratings,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = new SubmitPeerFeedbackRequest(
                ratings.Select(rating => new PlayerRatingRequest(rating.PlayerId, rating.Rating)).ToArray(),
                ratings.Where(rating => rating.IsLiked).Select(rating => rating.PlayerId).ToArray(),
                ratings.FirstOrDefault(rating => rating.IsMvp)?.PlayerId);
            // One key per match's feedback submission, reused until success, so a retry after a
            // dropped response replays instead of double-submitting (same contract as
            // ApiSessionAdminClient — see the idempotency note there).
            var key = _feedbackIdempotencyKeysByMatchId.GetOrAdd(
                matchId,
                static _ => Guid.NewGuid().ToString("N"));
            using var request = new HttpRequestMessage(HttpMethod.Post, $"stats/matches/{matchId}/feedback")
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            _feedbackIdempotencyKeysByMatchId.TryRemove(matchId, out _);
            return ClientCommandResult.Success;
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
