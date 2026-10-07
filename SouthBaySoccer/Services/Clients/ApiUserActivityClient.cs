using System.Net.Http.Json;
using SouthBaySoccer.Contracts.UserActivity;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiUserActivityClient(HttpClient httpClient) : IUserActivityClient
{
    public async Task<UserActivityPageDto> GetAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"admin/user-activity?page={page}&pageSize={pageSize}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserActivityPageDto>(cancellationToken)
            ?? throw new InvalidOperationException("The activity response was empty.");
    }
}
