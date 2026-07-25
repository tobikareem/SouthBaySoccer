using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Groups;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiGroupsClient(HttpClient httpClient) : IGroupsClient
{
    public async Task<IReadOnlyList<GroupChatDto>> GetAvailableGroupsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("groups", cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AvailableGroupsResponse>(
            cancellationToken: cancellationToken);
        return payload?.Groups ?? [];
    }

    public async Task<MyGroupsResponse> GetMyGroupsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("players/me/groups", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MyGroupsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The groups service returned an empty response.");
    }

    public async Task<MyGroupsResponse> LinkAsync(string groupExternalId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "players/me/groups/link",
            new LinkGroupRequest(groupExternalId),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MyGroupsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The groups service returned an empty response.");
    }
}
