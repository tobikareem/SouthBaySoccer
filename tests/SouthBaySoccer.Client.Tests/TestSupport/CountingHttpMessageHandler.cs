using System.Net;
using System.Net.Http;

namespace SouthBaySoccer.Client.Tests.TestSupport;

/// <summary>
/// Shared terminal <see cref="HttpMessageHandler"/> for HTTP request-count regression tests
/// (Phase 0.2 of <c>_specs/perf/2026-07-performance-review.md</c>). Thread-safely records every
/// request as <c>(Method, PathAndQuery)</c> and replays canned responses registered per path
/// prefix; unregistered paths get a 200 with an empty JSON object body. When several registered
/// prefixes match, the longest wins, so <c>/sessions/{id}/roster</c> beats <c>/sessions</c>.
/// </summary>
public sealed class CountingHttpMessageHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<(HttpMethod Method, string PathAndQuery)> _requests = [];
    private readonly List<(string PathPrefix, Func<HttpResponseMessage> ResponseFactory)> _cannedResponses = [];

    /// <summary>Snapshot of every request sent through this handler, in send order.</summary>
    public IReadOnlyList<(HttpMethod Method, string PathAndQuery)> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Number of recorded requests whose path-and-query starts with <paramref name="pathPrefix"/>.</summary>
    public int Count(string pathPrefix)
    {
        lock (_gate)
        {
            return _requests.Count(request =>
                request.PathAndQuery.StartsWith(pathPrefix, StringComparison.Ordinal));
        }
    }

    /// <summary>Clears the recorded requests; registered canned responses are kept.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _requests.Clear();
        }
    }

    /// <summary>Registers a 200 response with the given JSON body for paths starting with <paramref name="pathPrefix"/>.</summary>
    public void RegisterJson(string pathPrefix, string json) =>
        Register(pathPrefix, () => JsonResponse(HttpStatusCode.OK, json));

    /// <summary>Registers a body-less response (e.g. 204) for paths starting with <paramref name="pathPrefix"/>.</summary>
    public void RegisterStatus(string pathPrefix, HttpStatusCode statusCode) =>
        Register(pathPrefix, () => new HttpResponseMessage(statusCode));

    private void Register(string pathPrefix, Func<HttpResponseMessage> responseFactory)
    {
        lock (_gate)
        {
            _cannedResponses.Add((pathPrefix, responseFactory));
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var pathAndQuery = request.RequestUri!.PathAndQuery;
        Func<HttpResponseMessage>? responseFactory;
        lock (_gate)
        {
            _requests.Add((request.Method, pathAndQuery));
            responseFactory = _cannedResponses
                .Where(canned => pathAndQuery.StartsWith(canned.PathPrefix, StringComparison.Ordinal))
                .OrderByDescending(canned => canned.PathPrefix.Length)
                .Select(canned => canned.ResponseFactory)
                .FirstOrDefault();
        }

        return Task.FromResult(responseFactory?.Invoke() ?? JsonResponse(HttpStatusCode.OK, "{}"));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
}
