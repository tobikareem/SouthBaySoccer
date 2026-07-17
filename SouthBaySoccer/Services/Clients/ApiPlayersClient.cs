using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Players;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiPlayersClient(HttpClient httpClient) : IPlayersClient
{
    public async Task<PlayerDirectoryDto> GetDirectoryAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("players/directory", cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PlayerDirectoryDto>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The players service returned an empty directory response.");
    }
}
