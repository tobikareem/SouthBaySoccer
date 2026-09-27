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
        string? detail = null,
        string? firstFieldError = null,
        string? problemType = null)
        : base(message, null, statusCode)
    {
        Title = title;
        Detail = detail;
        FirstFieldError = firstFieldError;
        ProblemType = problemType;
    }

    /// <summary>The RFC 7807 "title" from the server's problem-details body, if present.</summary>
    public string? Title { get; }

    /// <summary>The RFC 7807 "detail" from the server's problem-details body, if present.</summary>
    public string? Detail { get; }

    /// <summary>
    /// The first field-level message from the RFC 7807 "errors" validation extension (map of field
    /// name to message array), if the server included one.
    /// </summary>
    public string? FirstFieldError { get; }

    /// <summary>The RFC 7807 problem type URI, when supplied by the server.</summary>
    public string? ProblemType { get; }

    /// <summary>
    /// Best available user-safe message: the server's <see cref="FirstFieldError"/> (a specific field
    /// message beats the generic validation summary), falling back to <see cref="Detail"/>, falling
    /// back to <see cref="Title"/>, falling back to the raw <see cref="Exception.Message"/>.
    /// </summary>
    public string UserMessage => FirstFieldError ?? Detail ?? Title ?? Message;
}
