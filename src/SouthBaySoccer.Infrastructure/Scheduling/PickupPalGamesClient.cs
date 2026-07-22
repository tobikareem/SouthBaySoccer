using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Infrastructure.Authentication;

namespace SouthBaySoccer.Infrastructure.Scheduling;

/// <summary>
/// HTTP client for the Pickup Pal active-games endpoint. Mirrors <see cref="PickupPalUserClient"/>:
/// typed HttpClient, lazy base address from <see cref="PickupPalApiOptions"/>, camelCase mapping via
/// explicit property names. The wire records include only the fields the import needs — WhatsApp
/// JIDs, group ids, and subscriber ids are never deserialized, so they cannot leak past this class.
/// </summary>
public sealed class PickupPalGamesClient(HttpClient httpClient, IOptions<PickupPalApiOptions> options)
    : IPickupPalGamesClient
{
    public async Task<IReadOnlyList<PickupPalGame>> GetActiveGamesAsync(
        CancellationToken cancellationToken = default)
    {
        httpClient.BaseAddress ??= new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");

        using var response = await httpClient.GetAsync("api/games/active", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ActiveGamesResponse>(
            cancellationToken: cancellationToken);
        if (payload?.Games is null)
        {
            return [];
        }

        return payload.Games
            .Where(game => !string.IsNullOrWhiteSpace(game.Id) && game.StartsAtUtc is not null)
            .Select(ToSanitizedGame)
            .ToArray();
    }

    private static PickupPalGame ToSanitizedGame(GameResponse game) =>
        new(
            game.Id!.Trim(),
            DateTime.SpecifyKind(game.StartsAtUtc!.Value, DateTimeKind.Utc),
            game.Location?.Trim() ?? string.Empty,
            game.MaxPlayers ?? 0,
            game.Status?.Trim() ?? string.Empty,
            game.Group?.GroupName?.Trim() ?? string.Empty,
            (game.Participants ?? [])
                .Where(participant => !string.IsNullOrWhiteSpace(participant.Id))
                .Select(participant => new PickupPalGameParticipantInfo(
                    participant.Id!.Trim(),
                    string.IsNullOrWhiteSpace(participant.DisplayName)
                        ? "Player"
                        : participant.DisplayName.Trim(),
                    participant.IsGuest ?? false,
                    participant.IsWaitlist ?? false,
                    participant.JoinedAtUtc is { } joinedAt
                        ? DateTime.SpecifyKind(joinedAt, DateTimeKind.Utc)
                        : DateTime.MinValue))
                .ToArray());

    private sealed record ActiveGamesResponse(
        [property: JsonPropertyName("games")] IReadOnlyList<GameResponse>? Games);

    private sealed record GameResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("dateTime")] DateTime? StartsAtUtc,
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("maxPlayers")] int? MaxPlayers,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("participants")] IReadOnlyList<ParticipantResponse>? Participants,
        [property: JsonPropertyName("group")] GroupResponse? Group);

    private sealed record ParticipantResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("isGuest")] bool? IsGuest,
        [property: JsonPropertyName("isWaitlist")] bool? IsWaitlist,
        [property: JsonPropertyName("joinedAt")] DateTime? JoinedAtUtc);

    private sealed record GroupResponse(
        [property: JsonPropertyName("groupName")] string? GroupName);
}
