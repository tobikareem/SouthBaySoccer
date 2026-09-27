using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Features.Announcements;
using SouthBaySoccer.Infrastructure.Authentication;

namespace SouthBaySoccer.Infrastructure.Announcements;

/// <summary>Posts announcements using Pickup Pal's documented group-chat send endpoint.</summary>
public sealed class PickupPalAnnouncementClient(
    HttpClient httpClient,
    IOptions<PickupPalApiOptions> options) : IPickupPalAnnouncementClient
{
    public async Task<PickupPalAnnouncementSendResult> SendAsync(
        string chatId,
        string message,
        CancellationToken cancellationToken = default)
    {
        httpClient.BaseAddress ??= new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/groupchat/send")
        {
            Content = JsonContent.Create(new SendMessageRequest(chatId, message))
        };
        if (!string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            request.Headers.TryAddWithoutValidation(options.Value.ApiKeyHeaderName, options.Value.ApiKey);
        }

        try
        {
            // Status alone defines delivery; provider bodies can contain private message/chat data.
            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return PickupPalAnnouncementSendResult.Sent;
            }

            return response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                || (int)response.StatusCode is >= 500 and <= 599
                    ? PickupPalAnnouncementSendResult.Retry
                    : PickupPalAnnouncementSendResult.Rejected;
        }
        catch (HttpRequestException)
        {
            return PickupPalAnnouncementSendResult.Retry;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PickupPalAnnouncementSendResult.Retry;
        }
    }

    private sealed record SendMessageRequest(
        [property: JsonPropertyName("chatId")] string ChatId,
        [property: JsonPropertyName("message")] string Message);
}
