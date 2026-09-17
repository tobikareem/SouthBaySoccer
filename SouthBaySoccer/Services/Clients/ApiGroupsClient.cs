using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Groups;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiGroupsClient(HttpClient httpClient) : IGroupsClient
{
    private const string EmptyResponseMessage = "The groups service returned an empty response.";

    public async Task<IReadOnlyList<GroupChatDto>> GetAvailableGroupsAsync(CancellationToken cancellationToken)
    {
        // GET groups now returns the membership catalogue. Legacy callers still get the old shape:
        // the catalogue carries no WhatsApp external id (the legacy link write is superseded by
        // RequestMembershipsAsync), so ExternalId is empty and IsLinked mirrors an Approved status.
        return ToLegacyGroups(await GetCatalogAsync(cancellationToken));
    }

    internal static IReadOnlyList<GroupChatDto> ToLegacyGroups(IReadOnlyList<GroupWithMembershipDto> catalog) =>
        catalog
            .Select(group => new GroupChatDto(
                group.Id,
                ExternalId: string.Empty,
                group.GroupName,
                group.MemberCount,
                IsLinked: group.MembershipStatus == GroupMembershipStatuses.Approved,
                IsPrimary: false))
            .ToArray();

    public async Task<MyGroupsResponse> GetMyGroupsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("players/me/groups", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MyGroupsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(EmptyResponseMessage);
    }

    public async Task<MyGroupsResponse> LinkAsync(string groupExternalId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "players/me/groups/link",
            new LinkGroupRequest(groupExternalId),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MyGroupsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(EmptyResponseMessage);
    }

    public async Task<IReadOnlyList<GroupWithMembershipDto>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        // The legacy GET groups keeps the old shape for older clients; the membership-aware
        // catalogue lives at groups/catalog.
        using var response = await httpClient.GetAsync("groups/catalog", cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GroupCatalogResponse>(
            cancellationToken: cancellationToken);
        return payload?.Groups ?? [];
    }

    public async Task<MyGroupMembershipsResponse> GetMyMembershipsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("players/me/memberships", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MyGroupMembershipsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(EmptyResponseMessage);
    }

    public async Task<MyGroupMembershipsResponse> RequestMembershipsAsync(
        IReadOnlyList<Guid> groupChatIds,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "players/me/memberships/requests",
            new RequestGroupMembershipsRequest(groupChatIds),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MyGroupMembershipsResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(EmptyResponseMessage);
    }

    public async Task LeaveAsync(Guid groupChatId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync($"players/me/memberships/{groupChatId:D}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<GroupMembersResponse> GetMembersAsync(Guid groupChatId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"groups/{groupChatId:D}/members", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GroupMembersResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(EmptyResponseMessage);
    }

    public Task ApproveAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken) =>
        PostMemberActionAsync(groupChatId, playerProfileId, "approve", cancellationToken);

    public Task DeclineAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken) =>
        PostMemberActionAsync(groupChatId, playerProfileId, "decline", cancellationToken);

    public Task RemoveMemberAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken) =>
        PostMemberActionAsync(groupChatId, playerProfileId, "remove", cancellationToken);

    public async Task AddMemberAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"groups/{groupChatId:D}/members",
            new AddGroupMemberRequest(playerProfileId),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetAdminAsync(Guid groupChatId, Guid playerProfileId, bool isAdmin, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"groups/{groupChatId:D}/admins",
            new SetGroupAdminRequest(playerProfileId, isAdmin),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<PlayerSearchResultDto>> SearchPlayersAsync(string query, CancellationToken cancellationToken)
    {
        // The only personal data allowed in a query string is the name fragment the admin typed.
        using var response = await httpClient.GetAsync(
            $"players/search?q={Uri.EscapeDataString(query)}",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<PlayerSearchResponse>(
            cancellationToken: cancellationToken);
        return payload?.Players ?? [];
    }

    private async Task PostMemberActionAsync(
        Guid groupChatId,
        Guid playerProfileId,
        string action,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(
            $"groups/{groupChatId:D}/members/{playerProfileId:D}/{action}",
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
