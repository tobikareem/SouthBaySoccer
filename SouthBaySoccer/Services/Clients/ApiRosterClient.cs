using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Rosters;
using SouthBaySoccer.Contracts.Rsvps;

namespace SouthBaySoccer.Services.Clients;

/// <summary>
/// API-backed roster client. Rosters come from GET sessions/{sessionId}/roster, which unions the
/// session's local RSVP/waitlist players with participants imported from Pickup Pal. RSVP writes
/// use the real endpoints with replay-protected idempotency keys.
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
        try
        {
            using var response = await httpClient.GetAsync($"sessions/{sessionId}/roster", cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RosterDto>(
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
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
        catch (ApiRequestException ex)
        {
            // Mirror ApiSessionsClient.ExecuteCommandAsync: any server-produced status (4xx or 5xx)
            // becomes an actionable failure; only pure connectivity faults propagate for Offline.
            return ClientCommandResult.Failure(ToErrorCode(ex.StatusCode), ex.UserMessage);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is not null)
        {
            return ClientCommandResult.Failure(ToErrorCode(ex.StatusCode), ex.Message);
        }
    }

    private static string ToErrorCode(HttpStatusCode? statusCode) =>
        statusCode is { } status ? $"http_{(int)status}" : "http_error";

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

    private static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");
}
