using System.Net;
using System.Text.Json;

namespace SouthBaySoccer.Services.Clients;

/// <summary>
/// Maps every non-2xx, non-304, non-401 API response into a thrown <see cref="ApiRequestException"/> carrying
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
        // Conditional polling intentionally consumes 304 responses in ApiGameDayClient. Let them
        // pass through just like successful responses instead of turning an unchanged draft into an
        // exception/backoff cycle. 401 still passes through for AuthenticationHandler refresh.
        if (response.IsSuccessStatusCode
            || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotModified)
        {
            return response;
        }

        var body = await ReadBodyAsync(response, cancellationToken);
        var (title, detail, firstFieldError, problemType) = ParseProblemDetails(body);
        var message = firstFieldError ?? detail ?? title ?? DefaultMessage(response.StatusCode, body);
        response.Dispose();
        throw new ApiRequestException(response.StatusCode, message, title, detail, firstFieldError, problemType);
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

    private static (string? Title, string? Detail, string? FirstFieldError, string? ProblemType) ParseProblemDetails(string? body)
    {
        if (body is null)
        {
            return (null, null, null, null);
        }

        try
        {
            var problem = JsonSerializer.Deserialize<ProblemDetailsPayload>(body, ProblemDetailsJsonOptions);
            var firstFieldError = problem?.Errors?
                .Values
                .FirstOrDefault(messages => messages is { Length: > 0 })
                ?.FirstOrDefault();
            return (problem?.Title, problem?.Detail, firstFieldError, problem?.Type);
        }
        catch (JsonException)
        {
            return (null, null, null, null);
        }
    }

    private static string DefaultMessage(HttpStatusCode statusCode, string? body) =>
        body ?? $"API request failed with status {(int)statusCode}.";

    /// <summary><see cref="Errors"/> mirrors RFC 7807's "errors" validation extension: field name to
    /// an array of messages for that field.</summary>
    private sealed record ProblemDetailsPayload(
        string? Type,
        string? Title,
        string? Detail,
        Dictionary<string, string[]>? Errors);
}
