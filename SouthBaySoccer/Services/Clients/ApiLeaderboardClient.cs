using System.Net;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Leaderboards;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiLeaderboardClient(HttpClient httpClient) : ILeaderboardClient
{
    public async Task<LeaderboardDto> GetRankingAsync(
        Guid seasonId,
        LeaderboardMetric metric,
        CancellationToken cancellationToken)
    {
        try
        {
            var path =
                $"stats/leaderboards?seasonId={seasonId:D}&metric={Uri.EscapeDataString(metric.ToString())}&page=1&pageSize=25";
            using var response = await httpClient.GetAsync(path, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<LeaderboardDto>(
                       cancellationToken: cancellationToken)
                   ?? throw new InvalidOperationException("The leaderboard service returned an empty response.");
        }
        catch (ApiRequestException ex) when (
            ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound &&
            IsUnknownSeasonFailure(ex))
        {
            // Player-facing season discovery is a recorded Sprint 03 contract gap: the client has
            // no endpoint that lists valid season ids, so an unknown/invalid season renders an
            // empty leaderboard instead of a dead-end error state.
            return new LeaderboardDto(seasonId, string.Empty, metric, string.Empty, []);
        }
    }

    private static bool IsUnknownSeasonFailure(ApiRequestException ex) =>
        ContainsSeasonHint(ex.UserMessage) ||
        ContainsSeasonHint(ex.Message);

    private static bool ContainsSeasonHint(string? value) =>
        value?.Contains("season", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Contains("seasonId", StringComparison.OrdinalIgnoreCase) == true;
}
