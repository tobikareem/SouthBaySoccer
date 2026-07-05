using System.Net;

namespace SouthBaySoccer.Services.Clients;

public sealed class ApiExceptionHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return response;
        }

        var message = await ReadSafeMessageAsync(response, cancellationToken);
        response.Dispose();
        throw new ApiRequestException(response.StatusCode, message);
    }

    private static async Task<string> ReadSafeMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(body)
                ? $"API request failed with status {(int)response.StatusCode}."
                : body;
        }
        catch
        {
            return $"API request failed with status {(int)response.StatusCode}.";
        }
    }
}
