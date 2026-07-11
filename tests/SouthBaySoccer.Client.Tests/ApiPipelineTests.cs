using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public sealed class ApiPipelineTests
{
    [Fact]
    public async Task AuthenticationHandler_WithStoredAccessToken_AttachesBearerToken()
    {
        HttpRequestMessage? observedRequest = null;
        var handler = new AuthenticationHandler(new StaticSessionRefresher("access-token"))
        {
            InnerHandler = new StubHttpMessageHandler(request =>
            {
                observedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };

        using var response = await client.GetAsync("profiles/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        observedRequest!.Headers.Authorization.Should().Be(
            new AuthenticationHeaderValue("Bearer", "access-token"));
    }

    [Fact]
    public async Task AuthenticationHandler_WhenGetReceivesUnauthorized_RefreshesAndRetriesOnce()
    {
        var refresher = new SequenceSessionRefresher("old-access", "new-access");
        var authorizations = new List<string?>();
        var handler = new AuthenticationHandler(refresher)
        {
            InnerHandler = new StubHttpMessageHandler(request =>
            {
                authorizations.Add(request.Headers.Authorization?.Parameter);
                return authorizations.Count == 1
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : new HttpResponseMessage(HttpStatusCode.OK);
            }),
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };

        using var response = await client.GetAsync("profiles/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        authorizations.Should().Equal("old-access", "new-access");
        refresher.ForceRefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task AuthenticationHandler_WhenPostReceivesUnauthorized_DoesNotReplay()
    {
        var requestCount = 0;
        var handler = new AuthenticationHandler(new SequenceSessionRefresher("old-access", "new-access"))
        {
            InnerHandler = new StubHttpMessageHandler(_ =>
            {
                requestCount++;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }),
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };

        using var response = await client.PostAsync("profiles/me", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        requestCount.Should().Be(1);
    }

    [Fact]
    public async Task AuthenticationSessionRefresher_WhenAccessTokenExpired_RefreshesOnceForConcurrentCallers()
    {
        var store = new InMemoryTokenStore(
            accessToken: "expired-access",
            refreshToken: "refresh-token",
            expiresAtUtc: DateTime.UtcNow.AddMinutes(-5));
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(x => x.RefreshAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticationTokensResponse(
                "new-access",
                "new-refresh",
                DateTime.UtcNow.AddMinutes(15)));
        var refresher = new AuthenticationSessionRefresher(store, authenticationClient.Object);

        var results = await Task.WhenAll(
            refresher.GetValidAccessTokenAsync(),
            refresher.GetValidAccessTokenAsync());

        results.Should().Equal("new-access", "new-access");
        store.RefreshToken.Should().Be("new-refresh");
        authenticationClient.Verify(
            x => x.RefreshAsync("refresh-token", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CorrelationIdHandler_WhenMissing_AddsCorrelationHeader()
    {
        HttpRequestMessage? observedRequest = null;
        var handler = new CorrelationIdHandler
        {
            InnerHandler = new StubHttpMessageHandler(request =>
            {
                observedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };

        using var response = await client.GetAsync("profiles/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        observedRequest!.Headers.Contains(CorrelationIdHandler.HeaderName).Should().BeTrue();
    }

    [Fact]
    public async Task ApiProfileClient_GetCurrentProfileAsync_MapsFunctionResponse()
    {
        var client = new ApiProfileClient(new HttpClient(new StubHttpMessageHandler(_ =>
            JsonResponse(
                """
                {
                    "playerProfileId": "11111111-1111-1111-1111-111111111111",
                    "identityUserId": "22222222-2222-2222-2222-222222222222",
                    "displayName": "Captain Tobi",
                    "preferredPosition": "st, rw, cm",
                    "photoUri": null,
                    "isGuest": false,
                    "role": "Player",
                    "emergencyContact": null
                }
                """)))
        {
            BaseAddress = new Uri("https://api.test/"),
        });

        var profile = await client.GetCurrentProfileAsync(CancellationToken.None);

        profile.Should().NotBeNull();
        profile!.PlayerId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        profile.DisplayName.Should().Be("Captain Tobi");
        profile.Subtitle.Should().Be("st, rw, cm");
        profile.Initials.Should().Be("CT");
    }

    [Fact]
    public async Task ApiProfileClient_GetProfileAsync_SendsProfileRouteAndMapsResponse()
    {
        HttpRequestMessage? observed = null;
        var playerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            observed = request;
            return JsonResponse(
                $$"""
                {
                    "playerId": "{{playerId}}",
                    "displayName": "Ada Johnson",
                    "subtitle": "Midfielder",
                    "initials": "AJ",
                    "careerStats": {
                        "matches": 12,
                        "goals": 4,
                        "assists": 5,
                        "averageRating": 4.6,
                        "mvpAwards": 2,
                        "likes": 7
                    },
                    "recentForm": [0, 1],
                    "pendingConfirmationNote": null,
                    "role": "Captain"
                }
                """);
        }))
        {
            BaseAddress = new Uri("https://api.test/"),
        };
        var client = new ApiProfileClient(httpClient);

        var profile = await client.GetProfileAsync(playerId, CancellationToken.None);

        observed!.Method.Should().Be(HttpMethod.Get);
        observed.RequestUri!.PathAndQuery.Should().Be($"/profiles/{playerId}");
        profile.Should().NotBeNull();
        profile!.DisplayName.Should().Be("Ada Johnson");
        profile.CareerStats.Matches.Should().Be(12);
        profile.RecentForm.Should().Equal(
            SouthBaySoccer.Contracts.Profiles.MatchResult.Win,
            SouthBaySoccer.Contracts.Profiles.MatchResult.Draw);
        profile.Role.Should().Be("Captain");
    }

    [Fact]
    public async Task ApiProfileClient_GetProfileAsync_WhenNotFound_ReturnsNull()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)))
        {
            BaseAddress = new Uri("https://api.test/"),
        };
        var client = new ApiProfileClient(httpClient);

        var profile = await client.GetProfileAsync(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            CancellationToken.None);

        profile.Should().BeNull();
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

    private sealed class StaticSessionRefresher(string accessToken) : IAuthenticationSessionRefresher
    {
        public Task<string?> GetValidAccessTokenAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(accessToken);
    }

    private sealed class SequenceSessionRefresher(params string[] accessTokens) : IAuthenticationSessionRefresher
    {
        private int index;

        public int ForceRefreshCount { get; private set; }

        public Task<string?> GetValidAccessTokenAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (forceRefresh)
            {
                ForceRefreshCount++;
            }

            var value = accessTokens[Math.Min(index, accessTokens.Length - 1)];
            index++;
            return Task.FromResult<string?>(value);
        }
    }

    private sealed class InMemoryTokenStore(
        string? accessToken,
        string? refreshToken,
        DateTime? expiresAtUtc) : ISecureTokenStore
    {
        public string? RefreshToken { get; private set; } = refreshToken;
        private string? accessToken = accessToken;
        private DateTime? expiresAtUtc = expiresAtUtc;

        public Task StoreAsync(AuthenticationTokensResponse tokens)
        {
            accessToken = tokens.AccessToken;
            RefreshToken = tokens.RefreshToken;
            expiresAtUtc = tokens.AccessTokenExpiresAtUtc;
            return Task.CompletedTask;
        }

        public Task<string?> GetAccessTokenAsync() => Task.FromResult(accessToken);

        public Task<DateTime?> GetAccessTokenExpiresAtUtcAsync() => Task.FromResult(expiresAtUtc);

        public Task<string?> GetRefreshTokenAsync() => Task.FromResult(RefreshToken);

        public Task ClearAsync()
        {
            accessToken = null;
            RefreshToken = null;
            expiresAtUtc = null;
            return Task.CompletedTask;
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
