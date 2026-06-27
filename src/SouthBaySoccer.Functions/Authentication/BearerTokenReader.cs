using Microsoft.Azure.Functions.Worker.Http;

namespace SouthBaySoccer.Functions.Authentication;

public static class BearerTokenReader
{
    private const string AuthorizationHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";

    public static string? TryRead(HttpHeadersCollection headers)
    {
        if (!headers.TryGetValues(AuthorizationHeader, out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = value[BearerPrefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
