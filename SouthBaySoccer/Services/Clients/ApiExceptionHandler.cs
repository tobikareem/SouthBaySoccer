using System.Net;
using System.Text.Json;

namespace SouthBaySoccer.Services.Clients;

/// <summary>
/// Maps every non-2xx, non-401 API response into a thrown <see cref="ApiRequestException"/> carrying
/// the status code and the RFC 7807 problem-details title/detail (when the body is one), so downstream
/// clients and page models can distinguish real server responses (validation, conflict, not-found) from
/// genuine connectivity failures instead of treating every <see cref="HttpRequestException"/> the same.
/// 401 responses pass through unchanged so <see cref="AuthenticationHandler"/> can refresh and replay.
/// </summary>
public sealed class ApiExceptionHandler : DelegatingHandler
{
    private static readonly JsonSerializerOptions ProblemDetailsJsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return response;
        }

        var body = await ReadBodyAsync(response, cancellationToken);
        var (title, detail) = ParseProblemDetails(body);
        var message = detail ?? title ?? DefaultMessage(response.StatusCode, body);
        response.Dispose();
        throw new ApiRequestException(response.StatusCode, message, title, detail);
    }

    private static async Task<string?> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch
        {
            return null;
        }
    }

    private static (string? Title, string? Detail) ParseProblemDetails(string? body)
    {
        if (body is null)
        {
            return (null, null);
        }

        try
        {
            var problem = JsonSerializer.Deserialize<ProblemDetailsPayload>(body, ProblemDetailsJsonOptions);
            return (problem?.Title, problem?.Detail);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string DefaultMessage(HttpStatusCode statusCode, string? body) =>
        body ?? $"API request failed with status {(int)statusCode}.";

    private sealed record ProblemDetailsPayload(string? Title, string? Detail);
}
