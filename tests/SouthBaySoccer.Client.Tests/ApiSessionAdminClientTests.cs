using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public sealed class ApiSessionAdminClientTests
{
    [Fact]
    public void AddSouthBaySoccerClients_ApiSelected_ResolvesRealSessionAdminClient()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<ISecureTokenStore>());

        services.AddSouthBaySoccerClients(
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api },
            new PickupPalOptions());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISessionAdminClient>()
            .Should().BeOfType<ApiSessionAdminClient>();
    }

    [Fact]
    public async Task CreateDraftAsync_SendsDraftRouteWithIdempotencyKey()
    {
        HttpRequestMessage? observed = null;
        string? observedBody = null;
        var sessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var client = CreateClient(request =>
        {
            observed = request;
            observedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(
                $$"""
                {
                  "isSuccess": true,
                  "sessionId": "{{sessionId}}",
                  "errorCode": null,
                  "errorMessage": null
                }
                """);
        });

        var result = await client.CreateDraftAsync(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.SessionId.Should().Be(sessionId);
        observed!.Method.Should().Be(HttpMethod.Post);
        observed.RequestUri!.PathAndQuery.Should().Be("/sessions/drafts");
        observed.Headers.Contains("Idempotency-Key").Should().BeTrue();
        observedBody.Should().Contain("\"venueName\":\"Marina Field\"");
    }

    [Fact]
    public async Task PublishAsync_SendsPublishRouteWithIdempotencyKey()
    {
        HttpRequestMessage? observed = null;
        var sessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var client = CreateClient(request =>
        {
            observed = request;
            return JsonResponse(
                $$"""
                {
                  "isSuccess": true,
                  "sessionId": "{{sessionId}}",
                  "errorCode": null,
                  "errorMessage": null
                }
                """);
        });

        var result = await client.PublishAsync(sessionId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        observed!.Method.Should().Be(HttpMethod.Post);
        observed.RequestUri!.PathAndQuery.Should().Be($"/sessions/{sessionId}/publish");
        observed.Headers.Contains("Idempotency-Key").Should().BeTrue();
    }

    [Fact]
    public async Task CreateVenueAsync_SendsVenueRouteAndMapsResponse()
    {
        HttpRequestMessage? observed = null;
        string? observedBody = null;
        var venueId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var client = CreateClient(request =>
        {
            observed = request;
            observedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(
                $$"""
                {
                  "venueId": "{{venueId}}",
                  "name": "New Park",
                  "locality": "Torrance",
                  "address": null,
                  "mapsProviderReference": null
                }
                """);
        });

        var venue = await client.CreateVenueAsync("New Park", "Torrance", null, CancellationToken.None);

        observed!.Method.Should().Be(HttpMethod.Post);
        observed.RequestUri!.PathAndQuery.Should().Be("/venues");
        observed.Headers.Contains("Idempotency-Key").Should().BeFalse();
        observedBody.Should().Contain("\"name\":\"New Park\"");
        venue.Id.Should().Be(venueId);
        venue.Name.Should().Be("New Park");
        venue.Locality.Should().Be("Torrance");
        venue.IsSaved.Should().BeTrue();
    }
    [Fact]
    public async Task SearchVenuesAsync_WithQuery_MapsVenueResponses()
    {
        HttpRequestMessage? observed = null;
        var venueId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var client = CreateClient(request =>
        {
            observed = request;
            return JsonResponse(
                $$"""
                [
                  {
                    "venueId": "{{venueId}}",
                    "name": "Marina Field",
                    "locality": "Redondo Beach",
                    "address": null,
                    "mapsProviderReference": null
                  }
                ]
                """);
        });

        var venues = await client.SearchVenuesAsync("Marina Field", CancellationToken.None);

        observed!.RequestUri!.PathAndQuery.Should().Be("/venues?query=Marina%20Field");
        venues.Should().ContainSingle();
        venues[0].Id.Should().Be(venueId);
        venues[0].Name.Should().Be("Marina Field");
        venues[0].IsSaved.Should().BeTrue();
    }

    private static ApiSessionAdminClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new HttpClient(new StubHttpMessageHandler(send))
        {
            BaseAddress = new Uri("https://api.test/"),
        });

    private static CreateSessionCommand ValidCommand() =>
        new(
            new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Unspecified),
            new TimeSpan(19, 40, 0),
            new TimeSpan(19, 30, 0),
            new TimeSpan(19, 40, 0),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "Marina Field",
            "7v7",
            20,
            2,
            new TimeSpan(18, 30, 0));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}

