using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Stats;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiStatsClient(HttpClient httpClient) : IStatsClient
{
    // One key per in-flight write, reused until the server answers definitively, so a retry after a
    // dropped response replays instead of double-submitting (same contract as ApiSessionAdminClient).
    private readonly ConcurrentDictionary<string, string> _idempotencyKeys = new();

    public async Task<MatchStatsDto?> GetMatchStatsAsync(Guid matchId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"stats/matches/{matchId}/me", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MatchStatsDto>(cancellationToken: cancellationToken);
    }

    public Task<ClientCommandResult> SubmitStatsAsync(
        Guid matchId,
        int goals,
        int assists,
        CancellationToken cancellationToken)
    {
        var operation = $"submit:{matchId}:{goals}:{assists}";
        return ExecuteCommandAsync(operation, async key =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"stats/matches/{matchId}/submissions")
            {
                Content = JsonContent.Create(new SubmitMatchStatsRequest(goals, assists)),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        });
    }

    public Task<ClientCommandResult> ConfirmStatsAsync(
        Guid matchId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var operation = $"confirm:{matchId}:{playerId}";
        return ExecuteCommandAsync(operation, async key =>
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"stats/matches/{matchId}/submissions/{playerId}/confirm");
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        });
    }

    public async Task<IReadOnlyList<RateableTeammateDto>> GetRateableTeammatesAsync(
        Guid matchId,
        Guid raterId,
        CancellationToken cancellationToken)
    {
        // The rater is identified by the bearer token; the server excludes them from its own list
        // (INV-8), so raterId never travels in the request.
        using var response = await httpClient.GetAsync(
            $"stats/matches/{matchId}/rateable-teammates",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<RateableTeammateDto>>(
                   cancellationToken: cancellationToken)
               ?? [];
    }

    public async Task<ClientCommandResult> SubmitRatingsAsync(
        Guid matchId,
        Guid raterId,
        IReadOnlyList<TeammateRatingDto> ratings,
        CancellationToken cancellationToken)
    {
        var operation = $"feedback:{matchId}";
        return await ExecuteCommandAsync(operation, async key =>
        {
            var body = new SubmitPeerFeedbackRequest(
                ratings.Select(rating => new PlayerRatingRequest(rating.PlayerId, rating.Rating)).ToArray(),
                ratings.Where(rating => rating.IsLiked).Select(rating => rating.PlayerId).ToArray(),
                ratings.FirstOrDefault(rating => rating.IsMvp)?.PlayerId);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"stats/matches/{matchId}/feedback")
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        });
    }

    private async Task<ClientCommandResult> ExecuteCommandAsync(
        string operation,
        Func<string, Task<ClientCommandResult>> send)
    {
        var key = _idempotencyKeys.GetOrAdd(operation, static _ => Guid.NewGuid().ToString("N"));
        try
        {
            var result = await send(key);
            _idempotencyKeys.TryRemove(operation, out _);
            return result;
        }
        catch (ApiRequestException ex) when (IsClientError(ex.StatusCode))
        {
            // A 4xx is a definitive answer: retrying with the same key would only replay a rejection.
            _idempotencyKeys.TryRemove(operation, out _);
            return ClientCommandResult.Failure($"http_{(int)ex.StatusCode!.Value}", ex.UserMessage);
        }
        catch (HttpRequestException ex) when (IsClientError(ex.StatusCode))
        {
            _idempotencyKeys.TryRemove(operation, out _);
            return ClientCommandResult.Failure($"http_{(int)ex.StatusCode!.Value}", ex.Message);
        }
    }

    private static bool IsClientError(HttpStatusCode? statusCode) =>
        statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;
}
