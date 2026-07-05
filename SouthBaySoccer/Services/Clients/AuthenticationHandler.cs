using System.Net;
using System.Net.Http.Headers;

namespace SouthBaySoccer.Services.Clients;

public sealed class AuthenticationHandler(IAuthenticationSessionRefresher sessionRefresher) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (IsAnonymousAuthEndpoint(request.RequestUri))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var accessToken = await sessionRefresher.GetValidAccessTokenAsync(cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || !CanReplay(request))
        {
            return response;
        }

        response.Dispose();
        var refreshedAccessToken = await sessionRefresher.GetValidAccessTokenAsync(
            forceRefresh: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(refreshedAccessToken))
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                RequestMessage = request,
            };
        }

        var replay = CloneForReplay(request);
        replay.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshedAccessToken);
        return await base.SendAsync(replay, cancellationToken);
    }

    private static bool IsAnonymousAuthEndpoint(Uri? uri)
    {
        if (uri is null)
        {
            return false;
        }

        var path = uri.AbsolutePath.Trim('/').ToLowerInvariant();
        return path.EndsWith("auth/pickuppal/phone/sign-in", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("auth/refresh", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("auth/whatsapp/challenges", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("auth/whatsapp/challenges/verify", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanReplay(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
        || request.Method == HttpMethod.Head
        || request.Method == HttpMethod.Options
        || request.Method == HttpMethod.Delete;

    private static HttpRequestMessage CloneForReplay(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}



