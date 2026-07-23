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
        // The seasonId argument is a seed-era placeholder the page model carries; the server
        // resolves the current season itself when none is supplied, so it is deliberately omitted.
        var path =
            $"stats/leaderboards?metric={Uri.EscapeDataString(metric.ToString())}&page=1&pageSize=25";
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LeaderboardDto>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The leaderboard service returned an empty response.");
    }
}
