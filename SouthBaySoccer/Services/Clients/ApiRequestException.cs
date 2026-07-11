using System.Net;

namespace SouthBaySoccer.Services.Clients;

/// <summary>
/// Thrown by <see cref="ApiExceptionHandler"/> for any non-2xx, non-401 API response. Carries the
/// response <see cref="HttpRequestException.StatusCode"/> plus the RFC 7807 problem-details
/// <see cref="Title"/>/<see cref="Detail"/> so callers can branch on the failure and surface the
/// server's actual message instead of a generic connectivity error.
/// </summary>
public sealed class ApiRequestException : HttpRequestException
{
    public ApiRequestException(
        HttpStatusCode statusCode,
        string message,
        string? title = null,
        string? detail = null)
        : base(message, null, statusCode)
    {
        Title = title;
        Detail = detail;
    }

    /// <summary>The RFC 7807 "title" from the server's problem-details body, if present.</summary>
    public string? Title { get; }

    /// <summary>The RFC 7807 "detail" from the server's problem-details body, if present.</summary>
    public string? Detail { get; }

    /// <summary>
    /// Best available user-safe message: the server's <see cref="Detail"/>, falling back to
    /// <see cref="Title"/>, falling back to the raw <see cref="Exception.Message"/>.
    /// </summary>
    public string UserMessage => Detail ?? Title ?? Message;
}
