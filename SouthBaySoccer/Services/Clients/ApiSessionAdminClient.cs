using System.Net;
using System.Net.Http.Json;
using SouthBaySoccer.Contracts.Sessions;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiSessionAdminClient(HttpClient httpClient) : ISessionAdminClient
{
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
        using var response = await httpClient.GetAsync($"sessions/{sessionId}/admin-edit", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ManagedSessionEditDto>(
            cancellationToken: cancellationToken);
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
            cancellationToken,
            includeIdempotencyKey: false);
        response.EnsureSuccessStatusCode();
        var venue = await response.Content.ReadFromJsonAsync<VenueResponse>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The session admin service returned an empty venue response.");
        return ToVenueDto(venue);
    }

    public async Task<CreateSessionResult> CreateDraftAsync(
        CreateSessionCommand command,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
            HttpMethod.Post,
            "sessions/drafts",
            command,
            cancellationToken);
        return await ReadCreateSessionResultAsync(response, cancellationToken);
    }

    public async Task<CreateSessionResult> UpdateSessionAsync(
        Guid sessionId,
        CreateSessionCommand command,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
            HttpMethod.Put,
            $"sessions/{sessionId}",
            command,
            cancellationToken);
        return await ReadCreateSessionResultAsync(response, cancellationToken);
    }

    public async Task<CreateSessionResult> PublishAsync(Guid draftId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"sessions/{draftId}/publish");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadCreateSessionResultAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        T body,
        CancellationToken cancellationToken,
        bool includeIdempotencyKey = true)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        if (includeIdempotencyKey)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        }

        return await httpClient.SendAsync(request, cancellationToken);
    }

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

