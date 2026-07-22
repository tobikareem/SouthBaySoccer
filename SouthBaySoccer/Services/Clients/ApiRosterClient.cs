using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Contracts.Rosters;
using SouthBaySoccer.Contracts.Rsvps;

namespace SouthBaySoccer.Services.Clients;

/// <summary>
/// API-backed roster client. The backend has no roster read endpoint yet (GET
/// sessions/{sessionId}/roster is a recorded Sprint 03 API-0 gap), so the roster is composed from
/// the caller's own RSVP state only: the current player appears in the going or waitlist list when
/// applicable and every other player is omitted rather than faked. RSVP writes use the real
/// endpoints with replay-protected idempotency keys.
/// </summary>
public sealed class ApiRosterClient(HttpClient httpClient) : IRosterClient
{
    private const string GoingState = "Going";
    private const string WaitlistedState = "Waitlisted";

    // One key per session per operation kind, reused until the server acknowledges success, so a
    // retry after a dropped response replays the stored result instead of double-submitting (same
    // contract as ApiSessionAdminClient — see the idempotency note there).
    private readonly ConcurrentDictionary<Guid, string> _submitIdempotencyKeysBySessionId = new();
    private readonly ConcurrentDictionary<Guid, string> _cancelIdempotencyKeysBySessionId = new();

    public async Task<RosterDto?> GetRosterAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        RsvpResponseDto? myRsvp;
        try
        {
            myRsvp = await GetMyRsvpAsync(sessionId, cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // GET rsvp/me 404s only when the caller's player profile is missing (unknown sessions
            // return 204), so an empty roster is the honest shape; GetSessionAsync's null result
            // owns the missing-session state.
            return new RosterDto(sessionId, [], []);
        }

        if (myRsvp is null || (myRsvp.State != GoingState && myRsvp.State != WaitlistedState))
        {
            return new RosterDto(sessionId, [], []);
        }

        var self = await GetSelfSummaryAsync(cancellationToken);
        if (self is null)
        {
            return new RosterDto(sessionId, [], []);
        }

        return myRsvp.State == GoingState
            ? new RosterDto(sessionId, [new RosterEntryDto(self, IsCurrentPlayer: true)], [])
            : new RosterDto(sessionId, [], [new WaitlistEntryDto(self, myRsvp.WaitlistPosition ?? 1)]);
    }

    public async Task<ClientCommandResult> SetRsvpIntentAsync(
        Guid sessionId,
        bool isGoing,
        CancellationToken cancellationToken)
    {
        try
        {
            return isGoing
                ? await SubmitGoingAsync(sessionId, cancellationToken)
                : await CancelAsync(sessionId, cancellationToken);
        }
        catch (ApiRequestException ex) when (IsClientError(ex.StatusCode))
        {
            return ClientCommandResult.Failure($"http_{(int)ex.StatusCode!.Value}", ex.UserMessage);
        }
        catch (HttpRequestException ex) when (IsClientError(ex.StatusCode))
        {
            return ClientCommandResult.Failure($"http_{(int)ex.StatusCode!.Value}", ex.Message);
        }
    }

    private async Task<ClientCommandResult> SubmitGoingAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var key = _submitIdempotencyKeysBySessionId.GetOrAdd(sessionId, static _ => NewIdempotencyKey());
        using var request = new HttpRequestMessage(HttpMethod.Post, $"sessions/{sessionId}/rsvp")
        {
            Content = JsonContent.Create(new SubmitRsvpRequest(GoingState)),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RsvpResponseDto>(
            cancellationToken: cancellationToken);
        _submitIdempotencyKeysBySessionId.TryRemove(sessionId, out _);

        // The server may accept a Going intent as Waitlisted when the session fills concurrently.
        // Preserve that server state; a future roster projection can expose the richer waitlist UI.
        return ClientCommandResult.Success;
    }

    private async Task<ClientCommandResult> CancelAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var key = _cancelIdempotencyKeysBySessionId.GetOrAdd(sessionId, static _ => NewIdempotencyKey());
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"sessions/{sessionId}/rsvp");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        _cancelIdempotencyKeysBySessionId.TryRemove(sessionId, out _);
        return ClientCommandResult.Success;
    }

    private async Task<RsvpResponseDto?> GetMyRsvpAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"sessions/{sessionId}/rsvp/me", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RsvpResponseDto>(
            cancellationToken: cancellationToken);
    }

    private async Task<PlayerSummaryDto?> GetSelfSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("profiles/me", cancellationToken);
            response.EnsureSuccessStatusCode();
            var profile = await response.Content.ReadFromJsonAsync<MyProfileResponse>(
                cancellationToken: cancellationToken);
            if (profile is null)
            {
                return null;
            }

            return new PlayerSummaryDto(
                profile.PlayerProfileId,
                profile.DisplayName,
                ToInitials(profile.DisplayName),
                profile.PreferredPosition,
                profile.IsGuest);
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static string ToInitials(string displayName)
    {
        var initials = string.Concat(
            displayName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0])));

        return string.IsNullOrWhiteSpace(initials) ? "SB" : initials;
    }

    private static bool IsClientError(HttpStatusCode? statusCode) =>
        statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;

    private static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");
}
