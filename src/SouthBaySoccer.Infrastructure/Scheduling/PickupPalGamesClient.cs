using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Infrastructure.Authentication;

namespace SouthBaySoccer.Infrastructure.Scheduling;

/// <summary>
/// HTTP client for the Pickup Pal games surface: the active feed, a single game, roster
/// add/remove, and game create/update/terminate for app-published sessions. Mirrors <see cref="PickupPalUserClient"/>: typed HttpClient, lazy base address from
/// <see cref="PickupPalApiOptions"/>, camelCase mapping via explicit property names. The wire
/// records include only the fields the import needs — WhatsApp JIDs, group ids, and subscriber ids
/// are never deserialized, so they cannot leak past this class.
/// <para>
/// <b>URI-logging ban:</b> this client takes no <c>ILogger</c> and attaches no message handlers, and
/// nothing may record its request URIs or bodies (a test guards the constructor).
/// </para>
/// <para>
/// Roster outcomes are classified from Pickup Pal's literal error strings (both body shapes from
/// the account-creation contract section 4). The matched substrings are documented in
/// <c>_specs/stories/RSVP-9-pickup-pal-roster-sync/design.md</c> and are assumptions until Pickup
/// Pal publishes a Games error catalogue.
/// </para>
/// </summary>
public sealed class PickupPalGamesClient(HttpClient httpClient, IOptions<PickupPalApiOptions> options)
    : IPickupPalGamesClient
{
    private const string UnavailableMessage = "Pickup Pal is unavailable right now. Try again later.";
    private const string WhatsAppGroupGameType = "WHATSAPP_GROUP";
    private const string SoccerSport = "SOCCER";

    public async Task<IReadOnlyList<PickupPalGame>> GetActiveGamesAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/games/active", content: null, cancellationToken);
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
            .Where(IsUsableGame)
            .Select(ToSanitizedGame)
            .ToArray();
    }

    public async Task<PickupPalGame?> GetGameAsync(string gameId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"api/games/{Uri.EscapeDataString(gameId)}",
            content: null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ApplicationServiceUnavailableException(UnavailableMessage);
        }

        GameResponse? game;
        try
        {
            // The single-game route answers with the same shape as one active-feed item; tolerate
            // a { "game": {...} } envelope should Pickup Pal wrap it.
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var element = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("game", out var nested)
                && nested.ValueKind == JsonValueKind.Object
                ? nested
                : root;
            game = element.Deserialize<GameResponse>();
        }
        catch (JsonException)
        {
            throw new ApplicationServiceUnavailableException(UnavailableMessage);
        }

        return game is not null && IsUsableGame(game) ? ToSanitizedGame(game) : null;
    }

    public async Task<PickupPalRosterPushResult> AddPlayerAsync(
        string gameId,
        string playerId,
        string playerName,
        CancellationToken cancellationToken = default)
    {
        // playerNumber is deliberately omitted: it is optional when playerId is given, and sending
        // it would put a phone number on the wire.
        using var response = await SendAsync(
            HttpMethod.Post,
            $"api/games/{Uri.EscapeDataString(gameId)}/players",
            JsonContent.Create(new AddPlayerPayload(playerId, playerName)),
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            // Pickup Pal confirmed (2026-09-16) that adding a player to a full game places them on
            // its waitlist and still answers success, so any 2xx is Applied regardless of body.
            return PickupPalRosterPushResult.Applied;
        }

        if (IsServerFailure(response))
        {
            throw new ApplicationServiceUnavailableException(UnavailableMessage);
        }

        var message = await ReadErrorMessageAsync(response, cancellationToken) ?? string.Empty;
        if (Contains(message, "already"))
        {
            return PickupPalRosterPushResult.AlreadyApplied;
        }

        if (Contains(message, "full"))
        {
            // Fallback only: kept in case an older Pickup Pal build still refuses a full game.
            return PickupPalRosterPushResult.GameFull;
        }

        if (IsPlayerNotFound(message))
        {
            // Pickup Pal does not know the user id we sent: terminal, but the game is still there.
            return PickupPalRosterPushResult.Rejected;
        }

        if (response.StatusCode == HttpStatusCode.NotFound || IsGameNotFound(message))
        {
            return PickupPalRosterPushResult.GameNotFound;
        }

        return PickupPalRosterPushResult.Rejected;
    }

    public async Task<PickupPalRosterPushResult> RemovePlayerAsync(
        string gameId,
        string playerId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Delete,
            $"api/games/{Uri.EscapeDataString(gameId)}/players/{Uri.EscapeDataString(playerId)}",
            content: null,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return PickupPalRosterPushResult.Applied;
        }

        if (IsServerFailure(response))
        {
            throw new ApplicationServiceUnavailableException(UnavailableMessage);
        }

        var message = await ReadErrorMessageAsync(response, cancellationToken) ?? string.Empty;
        if (IsPlayerNotOnRoster(message))
        {
            // Not on the roster is exactly the state a removal asks for. Checked before the game
            // rule because "Player not found in game" mentions both.
            return PickupPalRosterPushResult.AlreadyApplied;
        }

        if (IsGameNotFound(message))
        {
            return PickupPalRosterPushResult.GameNotFound;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return PickupPalRosterPushResult.AlreadyApplied;
        }

        return PickupPalRosterPushResult.Rejected;
    }

    public async Task<PickupPalGameCreateResult> CreateGameAsync(
        PickupPalGameCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var (date, time) = FormatLocalStart(request.StartsAtUtc, request.TimeZoneId);
        // lat/lng are omitted on purpose: sessions carry no coordinates.
        var payload = new CreateGamePayload(
            WhatsAppGroupGameType,
            request.GroupId,
            date,
            time,
            request.Location,
            request.MaxPlayers,
            request.CreatorId,
            SoccerSport,
            request.TimeZoneId);

        using var response = await SendAsync(HttpMethod.Post, "api/games", JsonContent.Create(payload), cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var gameId = await ReadCreatedGameIdAsync(response, cancellationToken);
            return gameId is null
                ? new PickupPalGameCreateResult(PickupPalGameWriteResult.InvalidResponse, null)
                : new PickupPalGameCreateResult(PickupPalGameWriteResult.Applied, gameId);
        }

        return new PickupPalGameCreateResult(await ClassifyWriteFailureAsync(response, cancellationToken), null);
    }

    public async Task<PickupPalGameWriteResult> UpdateGameAsync(
        string gameId,
        PickupPalGameUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        // The contract example carries only location and maxPlayers; date/time/timezone are sent
        // when the start changed and Pickup Pal may ignore them.
        string? date = null;
        string? time = null;
        string? timeZone = null;
        if (request.StartsAtUtc is { } startsAtUtc)
        {
            (date, time) = FormatLocalStart(startsAtUtc, request.TimeZoneId);
            timeZone = request.TimeZoneId;
        }

        var payload = new UpdateGamePayload(request.Location, request.MaxPlayers, date, time, timeZone);
        using var response = await SendAsync(
            HttpMethod.Put,
            $"api/games/{Uri.EscapeDataString(gameId)}",
            JsonContent.Create(payload, options: OmitNullsJsonOptions),
            cancellationToken);

        return response.IsSuccessStatusCode
            ? PickupPalGameWriteResult.Applied
            : await ClassifyWriteFailureAsync(response, cancellationToken);
    }

    public async Task<PickupPalGameWriteResult> TerminateGameAsync(string gameId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Delete,
            $"api/games/{Uri.EscapeDataString(gameId)}",
            content: null,
            cancellationToken);

        return response.IsSuccessStatusCode
            ? PickupPalGameWriteResult.Applied
            : await ClassifyWriteFailureAsync(response, cancellationToken);
    }

    /// <summary>
    /// Maps a failed create/update/terminate: 5xx is retryable; 404 or a "game ... not found"
    /// message is <see cref="PickupPalGameWriteResult.GameNotFound"/>; any other 4xx (a 400 with a
    /// message included) is a terminal <see cref="PickupPalGameWriteResult.Rejected"/>. Only the
    /// classification leaves this method, never the message.
    /// </summary>
    private static async Task<PickupPalGameWriteResult> ClassifyWriteFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (IsServerFailure(response))
        {
            throw new ApplicationServiceUnavailableException(UnavailableMessage);
        }

        var message = await ReadErrorMessageAsync(response, cancellationToken) ?? string.Empty;
        if (response.StatusCode == HttpStatusCode.NotFound || IsGameNotFound(message))
        {
            return PickupPalGameWriteResult.GameNotFound;
        }

        return PickupPalGameWriteResult.Rejected;
    }

    /// <summary>
    /// Reads the created game's id, tolerating a <c>{ "game": {...} }</c> envelope and either
    /// <c>id</c> or <c>gameId</c> as the property name.
    /// </summary>
    private static async Task<string?> ReadCreatedGameIdAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var element = root.TryGetProperty("game", out var nested) && nested.ValueKind == JsonValueKind.Object
                ? nested
                : root;
            foreach (var propertyName in new[] { "id", "gameId" })
            {
                if (element.TryGetProperty(propertyName, out var id)
                    && id.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(id.GetString()))
                {
                    return id.GetString()!.Trim(); // non-null: the guard above rejects null and blank values.
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Formats a UTC start as the group-local <c>yyyy-MM-dd</c> and <c>HH:mm:ss</c> Pickup Pal expects.</summary>
    private static (string Date, string Time) FormatLocalStart(DateTime startsAtUtc, string timeZoneId)
    {
        var local = SessionAdminTimeZone.ToLocal(startsAtUtc, SessionAdminTimeZone.Resolve(timeZoneId));
        return (
            local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            local.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string route,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        httpClient.BaseAddress ??= new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");

        using var request = new HttpRequestMessage(method, route) { Content = content };
        var apiKey = options.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation(options.Value.ApiKeyHeaderName, apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new ApplicationServiceUnavailableException(UnavailableMessage);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout surfaces as TaskCanceledException without the caller's token set.
            throw new ApplicationServiceUnavailableException(UnavailableMessage);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // Our API key was rejected (or a proxy answered): retryable, never a roster outcome.
            response.Dispose();
            throw new ApplicationServiceUnavailableException(UnavailableMessage);
        }

        return response;
    }

    private static bool IsServerFailure(HttpResponseMessage response) => (int)response.StatusCode >= 500;

    private static bool IsGameNotFound(string message) =>
        Contains(message, "game") && Contains(message, "not found") && !IsPlayerNotFound(message);

    private static bool IsPlayerNotFound(string message) =>
        (Contains(message, "player") || Contains(message, "user") || Contains(message, "participant"))
        && Contains(message, "not found");

    private static bool IsPlayerNotOnRoster(string message) =>
        Contains(message, "not in")
        || Contains(message, "not a participant")
        || Contains(message, "not on")
        || IsPlayerNotFound(message);

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads both Pickup Pal error shapes: <c>{ "error": "text" }</c> on 4xx and
    /// <c>{ "error": { "message": "text", "status": n } }</c> from the unhandled-error handler.
    /// </summary>
    private static async Task<string?> ReadErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error))
            {
                return null;
            }

            return error.ValueKind switch
            {
                JsonValueKind.String => error.GetString(),
                JsonValueKind.Object when error.TryGetProperty("message", out var nested) &&
                                          nested.ValueKind == JsonValueKind.String => nested.GetString(),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsUsableGame(GameResponse game) =>
        !string.IsNullOrWhiteSpace(game.Id) && game.StartsAtUtc is not null;

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
                .Select(ToSanitizedParticipant)
                .ToArray(),
            // The group id is only carried so the import can match GroupChat.ExternalId; it is
            // JsonIgnore'd on the record and never logged.
            string.IsNullOrWhiteSpace(game.Group?.GroupId) ? null : game.Group.GroupId.Trim());

    private static PickupPalGameParticipantInfo ToSanitizedParticipant(ParticipantResponse participant)
    {
        // Normalize the phone once — both the hash and the masked display derive from it — and trim
        // the JID before hashing so trailing whitespace can't split one person's dedupe key.
        var normalizedPhone = NormalizePhone(participant.PhoneNumber);
        var whatsAppJid = string.IsNullOrWhiteSpace(participant.WhatsAppJid)
            ? null
            : participant.WhatsAppJid.Trim();

        return new PickupPalGameParticipantInfo(
            participant.Id!.Trim(),
            string.IsNullOrWhiteSpace(participant.DisplayName)
                ? "Player"
                : participant.DisplayName.Trim(),
            participant.IsGuest ?? false,
            participant.IsWaitlist ?? false,
            participant.JoinedAtUtc is { } joinedAt
                ? DateTime.SpecifyKind(joinedAt, DateTimeKind.Utc)
                : DateTime.MinValue,
            UserId: string.IsNullOrWhiteSpace(participant.UserId) ? null : participant.UserId.Trim(),
            // Raw phone numbers and WhatsApp JIDs never leave this class; only hashes (and a masked
            // phone for display) cross the boundary, matching how sign-in stores phone identity.
            PhoneNumberHash: normalizedPhone is { } phone ? AuthenticationHashing.Sha256(phone) : null,
            MaskedPhoneNumber: MaskPhone(normalizedPhone),
            WhatsAppJidHash: whatsAppJid is null ? null : AuthenticationHashing.Sha256(whatsAppJid));
    }

    private static string? NormalizePhone(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        // char.IsDigit to match PickupPalUserSyncService.NormalizePhone exactly — the hashes must
        // agree or import-created profiles would never dedupe against sign-in profiles.
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : $"+{digits}";
    }

    private static string? MaskPhone(string? normalizedPhone)
    {
        if (normalizedPhone is null)
        {
            return null;
        }

        var digits = normalizedPhone.TrimStart('+');
        return digits.Length <= 4 ? "***" : $"+******{digits[^4..]}";
    }

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
        [property: JsonPropertyName("joinedAt")] DateTime? JoinedAtUtc,
        [property: JsonPropertyName("userId")] string? UserId,
        [property: JsonPropertyName("phoneNumber")] string? PhoneNumber,
        [property: JsonPropertyName("whatsappJid")] string? WhatsAppJid);

    private sealed record GroupResponse(
        [property: JsonPropertyName("groupName")] string? GroupName,
        [property: JsonPropertyName("groupId")] string? GroupId = null);

    private sealed record AddPlayerPayload(
        [property: JsonPropertyName("playerId")] string PlayerId,
        [property: JsonPropertyName("playerName")] string PlayerName);

    private sealed record CreateGamePayload(
        [property: JsonPropertyName("gameType")] string GameType,
        [property: JsonPropertyName("groupId")] string GroupId,
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("time")] string Time,
        [property: JsonPropertyName("location")] string Location,
        [property: JsonPropertyName("maxPlayers")] int MaxPlayers,
        [property: JsonPropertyName("creatorId")] string CreatorId,
        [property: JsonPropertyName("sport")] string Sport,
        [property: JsonPropertyName("timezone")] string Timezone);

    private sealed record UpdateGamePayload(
        [property: JsonPropertyName("location")] string Location,
        [property: JsonPropertyName("maxPlayers")] int MaxPlayers,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("time")] string? Time,
        [property: JsonPropertyName("timezone")] string? Timezone);

    private static readonly JsonSerializerOptions OmitNullsJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
