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

    [Fact]
    public async Task GetSessionForEditAsync_WhenFound_ReturnsSession_ThroughPipeline()
    {
        var sessionId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var client = CreatePipelineClient(_ => JsonResponse(
            $$"""
            {
              "sessionId": "{{sessionId}}",
              "command": {
                "gameDateLocal": "2026-07-11T00:00:00",
                "startTimeLocal": "19:40:00",
                "checkInOpenLocal": "19:30:00",
                "checkInCloseLocal": "19:40:00",
                "venueId": "44444444-4444-4444-4444-444444444444",
                "venueName": "Marina Field",
                "format": "7v7",
                "capacity": 20,
                "teamCount": 2,
                "rsvpDeadlineLocal": "18:30:00"
              },
              "isPublished": true
            }
            """));

        var session = await client.GetSessionForEditAsync(sessionId, CancellationToken.None);

        session.Should().NotBeNull();
        session!.SessionId.Should().Be(sessionId);
        session.Command.VenueName.Should().Be("Marina Field");
    }

    [Fact]
    public async Task GetSessionForEditAsync_WhenNotFound_ReturnsNull_ThroughPipeline()
    {
        // ApiSessionAdminClient is registered behind ApiExceptionHandler in production, which throws
        // ApiRequestException for the 404 instead of returning the response object. Compose the real
        // handler here so this test would fail against the old dead `response.StatusCode == NotFound`
        // check (that check never runs because the pipeline already threw).
        var client = CreatePipelineClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var session = await client.GetSessionForEditAsync(Guid.NewGuid(), CancellationToken.None);

        session.Should().BeNull();
    }

    [Fact]
    public async Task CreateDraftAsync_WhenBadRequestProblemDetails_ReturnsFailureWithServerMessage_ThroughPipeline()
    {
        var client = CreatePipelineClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """
                {
                    "title": "Validation failed",
                    "detail": "Capacity must be at least 1.",
                    "status": 400
                }
                """,
                System.Text.Encoding.UTF8,
                "application/problem+json"),
        });

        var result = await client.CreateDraftAsync(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Capacity must be at least 1.");
    }

    [Fact]
    public async Task PublishAsync_WhenConflictProblemDetails_ReturnsFailureWithServerMessage_ThroughPipeline()
    {
        var client = CreatePipelineClient(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """
                {
                    "title": "Conflict",
                    "detail": "This draft was already published.",
                    "status": 409
                }
                """,
                System.Text.Encoding.UTF8,
                "application/problem+json"),
        });

        var result = await client.PublishAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("This draft was already published.");
    }

    [Fact]
    public async Task CreateDraftAsync_WhenServerError_PropagatesApiRequestException_ThroughPipeline()
    {
        // 5xx is not a client-fixable validation error, so it should NOT be swallowed into a
        // CreateSessionResult.Failure — it must keep propagating so the caller's generic error handling
        // (not the offline copy, not a silent failure result) applies.
        var client = CreatePipelineClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(
                """
                {
                    "title": "Unexpected error",
                    "detail": "An unexpected error occurred.",
                    "status": 500
                }
                """,
                System.Text.Encoding.UTF8,
                "application/problem+json"),
        });

        var act = () => client.CreateDraftAsync(ValidCommand(), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ApiRequestException>();
        thrown.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    private static ApiSessionAdminClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new HttpClient(new StubHttpMessageHandler(send))
        {
            BaseAddress = new Uri("https://api.test/"),
        });

    private static ApiSessionAdminClient CreatePipelineClient(Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var handler = new ApiExceptionHandler
        {
            InnerHandler = new StubHttpMessageHandler(send),
        };
        return new ApiSessionAdminClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.test/"),
        });
    }

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

