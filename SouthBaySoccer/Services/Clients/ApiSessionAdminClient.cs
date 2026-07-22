using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Contracts.Common;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiSessionAdminClient(HttpClient httpClient) : ISessionAdminClient
{
    // ADMIN-4 idempotency: the server's IdempotentRequestExecutor replays the stored response when the
    // SAME Idempotency-Key is presented again, and rejects a key reused with a different request body.
    // A fresh key per HTTP call defeated that protection on retry (a lost response looked like a brand
    // new operation, so a retried create could mint a duplicate draft). Each logical operation therefore
    // owns exactly one key, created lazily on first attempt and cleared only once that operation
    // succeeds, so a retry after a dropped response reuses the key (server replays instead of
    // duplicating) while a genuinely new operation gets a fresh one. Update/publish are keyed per
    // target id (not a single field) because one client instance can update or publish several
    // different sessions across the page's lifetime.
    private string? _createDraftIdempotencyKey;
    private readonly ConcurrentDictionary<Guid, string> _updateIdempotencyKeysBySessionId = new();
    private readonly ConcurrentDictionary<Guid, string> _publishIdempotencyKeysByDraftId = new();

    public async Task<CreateSessionDefaultsDto> GetDefaultsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("sessions/admin/create-defaults", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreateSessionDefaultsDto>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("The session admin service returned an empty defaults response.");
    }

    public async Task<IReadOnlyList<ManagedSessionDto>> ListManagedSessionsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("sessions/admin/managed", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ManagedSessionDto>>(
                   cancellationToken: cancellationToken)
               ?? Array.Empty<ManagedSessionDto>();
    }

    public async Task<ManagedSessionEditDto?> GetSessionForEditAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"sessions/{sessionId}/admin-edit", cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ManagedSessionEditDto>(
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<VenueDto>> SearchVenuesAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(query)
            ? "venues"
            : $"venues?query={Uri.EscapeDataString(query)}";
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        var venues = await response.Content.ReadFromJsonAsync<IReadOnlyList<VenueResponse>>(
            cancellationToken: cancellationToken);

        return venues?.Select(ToVenueDto).ToArray() ?? [];
    }

    public async Task<VenueDto> CreateVenueAsync(
        string name,
        string locality,
        string? address,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
            HttpMethod.Post,
            "venues",
            new CreateVenueRequest(name, locality, address),
            idempotencyKey: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var venue = await response.Content.ReadFromJsonAsync<VenueResponse>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The session admin service returned an empty venue response.");
        return ToVenueDto(venue);
    }

    public Task<CreateSessionResult> CreateDraftAsync(
        CreateSessionCommand command,
        CancellationToken cancellationToken) =>
        ExecuteSessionResultAsync(async () =>
        {
            var key = _createDraftIdempotencyKey ??= NewIdempotencyKey();
            using var response = await SendJsonAsync(
                HttpMethod.Post,
                "sessions/drafts",
                command,
                key,
                cancellationToken);
            var result = await ReadCreateSessionResultAsync(response, cancellationToken);
            if (result.IsSuccess)
            {
                _createDraftIdempotencyKey = null;
            }

            return result;
        });

    public Task<CreateSessionResult> UpdateSessionAsync(
        Guid sessionId,
        CreateSessionCommand command,
        CancellationToken cancellationToken) =>
        ExecuteSessionResultAsync(async () =>
        {
            var key = _updateIdempotencyKeysBySessionId.GetOrAdd(sessionId, static _ => NewIdempotencyKey());
            using var response = await SendJsonAsync(
                HttpMethod.Put,
                $"sessions/{sessionId}",
                command,
                key,
                cancellationToken);
            var result = await ReadCreateSessionResultAsync(response, cancellationToken);
            if (result.IsSuccess)
            {
                _updateIdempotencyKeysBySessionId.TryRemove(sessionId, out _);
            }

            return result;
        });

    public Task<CreateSessionResult> PublishAsync(Guid draftId, CancellationToken cancellationToken) =>
        ExecuteSessionResultAsync(async () =>
        {
            var key = _publishIdempotencyKeysByDraftId.GetOrAdd(draftId, static _ => NewIdempotencyKey());
            using var request = new HttpRequestMessage(HttpMethod.Post, $"sessions/{draftId}/publish");
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var result = await ReadCreateSessionResultAsync(response, cancellationToken);
            if (result.IsSuccess)
            {
                _publishIdempotencyKeysByDraftId.TryRemove(draftId, out _);
            }

            return result;
        });

    public Task<ClientCommandResult> CancelSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        ExecuteCommandAsync(async () =>
        {
            using var response = await SendJsonAsync(
                HttpMethod.Post,
                $"sessions/{sessionId}/cancel",
                new CancelSessionRequest("Cancelled by administrator."),
                idempotencyKey: null,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        });

    public Task<ClientCommandResult> DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        ExecuteCommandAsync(async () =>
        {
            using var response = await httpClient.DeleteAsync($"sessions/{sessionId}", cancellationToken);
            response.EnsureSuccessStatusCode();
            return ClientCommandResult.Success;
        });

    /// <summary>
    /// Runs a create/update/publish operation, converting a 4xx <see cref="ApiRequestException"/> (the
    /// server's problem-details response) into a <see cref="CreateSessionResult.Failure"/> carrying the
    /// server's message. 401 never reaches here (handled upstream); 5xx and non-API failures propagate
    /// so the caller's generic/offline handling applies.
    /// </summary>
    private static async Task<CreateSessionResult> ExecuteSessionResultAsync(
        Func<Task<CreateSessionResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ApiRequestException ex) when (IsClientError(ex.StatusCode))
        {
            // ApiRequestException's constructor always takes a non-nullable HttpStatusCode, so
            // StatusCode is never null here; the ! only satisfies the inherited nullable signature.
            return CreateSessionResult.Failure($"http_{(int)ex.StatusCode!.Value}", ex.UserMessage);
        }
    }

    private static bool IsClientError(HttpStatusCode? statusCode) =>
        statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;

    private static async Task<ClientCommandResult> ExecuteCommandAsync(Func<Task<ClientCommandResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ApiRequestException ex) when (IsClientError(ex.StatusCode))
        {
            return ClientCommandResult.Failure($"http_{(int)ex.StatusCode!.Value}", ex.UserMessage);
        }
    }

    private async Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        T body,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");

    private static async Task<CreateSessionResult> ReadCreateSessionResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreateSessionResult>(
                   cancellationToken: cancellationToken)
               ?? CreateSessionResult.Failure("empty_response", "The session admin service returned an empty response.");
    }

    private static VenueDto ToVenueDto(VenueResponse venue) =>
        new(venue.VenueId, venue.Name, venue.Locality, true);
}
