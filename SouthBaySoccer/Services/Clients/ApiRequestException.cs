using System.Net;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiRequestException : HttpRequestException
{
    public ApiRequestException(HttpStatusCode statusCode, string message)
        : base(message, null, statusCode)
    {
    }
}
