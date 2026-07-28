using System.Globalization;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Announcements;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiAnnouncementsClient(HttpClient httpClient) : IAnnouncementsClient
{
    public async Task<AnnouncementFeedResponse> GetFeedAsync(
        Guid groupId,
        int limit,
        DateTime? beforeUtc,
        Guid? beforeId,
        CancellationToken cancellationToken)
    {
        if (beforeUtc.HasValue != beforeId.HasValue)
        {
            throw new ArgumentException("Announcement paging requires both beforeUtc and beforeId.");
        }

        var route = $"groups/{groupId}/announcements?limit={limit}";
        if (beforeUtc is not null)
        {
            route += $"&before={Uri.EscapeDataString(beforeUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}";
        }
        if (beforeId is not null)
        {
            route += $"&beforeId={beforeId.Value:D}";
        }

        using var response = await httpClient.GetAsync(route, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AnnouncementFeedResponse>(cancellationToken)
            ?? new AnnouncementFeedResponse(groupId, string.Empty, [], 0, null, null);
    }

    public async Task<MarkAnnouncementsReadResponse> MarkReadAsync(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(
            $"groups/{groupId}/announcements/read",
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MarkAnnouncementsReadResponse>(cancellationToken)
            ?? new MarkAnnouncementsReadResponse(groupId, DateTime.UnixEpoch, 0);
    }

    public async Task<UnreadAnnouncementsResponse> GetUnreadCountAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            "players/me/announcements/unread-count",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UnreadAnnouncementsResponse>(cancellationToken)
            ?? new UnreadAnnouncementsResponse(0);
    }

    public async Task<SentAnnouncementDto> PostAsync(
        Guid groupId,
        PostAnnouncementRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"groups/{groupId}/announcements")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SentAnnouncementDto>(cancellationToken)
            ?? throw new InvalidOperationException("The announcement response was empty.");
    }

    public async Task<SentAnnouncementsResponse> GetSentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"players/me/announcements/sent?limit={limit}",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SentAnnouncementsResponse>(cancellationToken)
            ?? new SentAnnouncementsResponse([]);
    }
}
