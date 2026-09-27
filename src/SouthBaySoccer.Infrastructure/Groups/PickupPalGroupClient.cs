using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Infrastructure.Authentication;

namespace SouthBaySoccer.Infrastructure.Groups;

/// <summary>
/// Read-only HTTP client for the Pickup Pal group-chat endpoints. Mirrors
/// <see cref="Scheduling.PickupPalGamesClient"/>: typed HttpClient, lazy base address from
/// <see cref="PickupPalApiOptions"/>, camelCase mapping via explicit property names. The API owner
/// exposes only GET endpoints, so this client never writes linkage back — our database owns it.
/// </summary>
public sealed class PickupPalGroupClient(HttpClient httpClient, IOptions<PickupPalApiOptions> options)
    : IPickupPalGroupClient
{
    public async Task<IReadOnlyList<PickupPalGroupChat>> GetAllGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        httpClient.BaseAddress ??= new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");

        using var response = await httpClient.GetAsync("api/groupchat/all", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GroupChatListResponse>(
            cancellationToken: cancellationToken);
        return ToSanitizedGroups(payload?.GroupChats);
    }

    public async Task<IReadOnlyList<PickupPalGroupChat>> GetLinkedGroupsAsync(
        string pickupPalUserId,
        CancellationToken cancellationToken = default)
    {
        httpClient.BaseAddress ??= new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");

        var route = $"api/groupchat/user/{Uri.EscapeDataString(pickupPalUserId)}/linked";
        using var response = await httpClient.GetAsync(route, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LinkedGroupsResponse>(
            cancellationToken: cancellationToken);
        return ToSanitizedGroups(payload?.Groups);
    }

    private static IReadOnlyList<PickupPalGroupChat> ToSanitizedGroups(IReadOnlyList<GroupChatResponse>? groups) =>
        (groups ?? [])
            .Where(group => !string.IsNullOrWhiteSpace(group.Id))
            .Select(group => new PickupPalGroupChat(
                group.Id!.Trim(),
                group.GroupName?.Trim() ?? string.Empty,
                string.IsNullOrWhiteSpace(group.LinkageCode) ? null : group.LinkageCode.Trim(),
                group.Status?.Trim() ?? string.Empty,
                group.WhatsAppMemberCount ?? 0,
                string.IsNullOrWhiteSpace(group.Timezone) ? null : group.Timezone.Trim()))
            .ToArray();

    private sealed record GroupChatListResponse(
        [property: JsonPropertyName("groupChats")] IReadOnlyList<GroupChatResponse>? GroupChats);

    private sealed record LinkedGroupsResponse(
        [property: JsonPropertyName("groups")] IReadOnlyList<GroupChatResponse>? Groups);

    private sealed record GroupChatResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("groupName")] string? GroupName,
        [property: JsonPropertyName("linkageCode")] string? LinkageCode,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("whatsappMemberCount")] int? WhatsAppMemberCount,
        [property: JsonPropertyName("timezone")] string? Timezone);
}
