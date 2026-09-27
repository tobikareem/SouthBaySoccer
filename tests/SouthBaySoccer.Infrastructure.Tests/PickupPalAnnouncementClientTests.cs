using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Features.Announcements;
using SouthBaySoccer.Infrastructure.Announcements;
using SouthBaySoccer.Infrastructure.Authentication;

namespace SouthBaySoccer.Infrastructure.Tests;

public sealed class PickupPalAnnouncementClientTests
{
    [Fact]
    public async Task SendAsync_WhenCalled_PostsDocumentedBodyWithoutIdentifiersInUrl()
    {
        var client = CreateClient(async (request, cancellationToken) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().Be(new Uri("https://pickup.example/api/groupchat/send"));
            request.Headers.Contains("X-Api-Key").Should().BeFalse();
            var content = request.Content ?? throw new InvalidOperationException("Expected request content.");
            content.Headers.ContentType?.MediaType.Should().Be("application/json");
            using var body = JsonDocument.Parse(await content.ReadAsStringAsync(cancellationToken));
            body.RootElement.EnumerateObject().Select(property => property.Name)
                .Should().BeEquivalentTo("chatId", "message");
            body.RootElement.GetProperty("chatId").GetString().Should().Be("group@g.us");
            body.RootElement.GetProperty("message").GetString().Should().Be("Tonight's game\nBring a ball.");
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await client.SendAsync("group@g.us", "Tonight's game\nBring a ball.");

        result.Should().Be(PickupPalAnnouncementSendResult.Sent);
    }

    [Fact]
    public async Task SendAsync_WhenApiKeyConfigured_SendsConfiguredHeader()
    {
        var client = CreateClient((request, _) =>
        {
            request.Headers.GetValues("X-Bot-Key").Should().Equal("test-key");
            request.RequestUri?.Query.Should().BeEmpty();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }, new PickupPalApiOptions
        {
            BaseUrl = "https://pickup.example/",
            ApiKey = "test-key",
            ApiKeyHeaderName = "X-Bot-Key"
        });

        var result = await client.SendAsync("group@g.us", "Game reminder");

        result.Should().Be(PickupPalAnnouncementSendResult.Sent);
    }

    [Theory]
    [InlineData(200, PickupPalAnnouncementSendResult.Sent)]
    [InlineData(201, PickupPalAnnouncementSendResult.Sent)]
    [InlineData(202, PickupPalAnnouncementSendResult.Sent)]
    [InlineData(204, PickupPalAnnouncementSendResult.Sent)]
    [InlineData(400, PickupPalAnnouncementSendResult.Rejected)]
    [InlineData(401, PickupPalAnnouncementSendResult.Rejected)]
    [InlineData(403, PickupPalAnnouncementSendResult.Rejected)]
    [InlineData(404, PickupPalAnnouncementSendResult.Rejected)]
    [InlineData(408, PickupPalAnnouncementSendResult.Retry)]
    [InlineData(429, PickupPalAnnouncementSendResult.Retry)]
    [InlineData(500, PickupPalAnnouncementSendResult.Retry)]
    [InlineData(503, PickupPalAnnouncementSendResult.Retry)]
    public async Task SendAsync_WhenProviderResponds_ClassifiesStatusWithoutReadingBody(
        int statusCode, PickupPalAnnouncementSendResult expected)
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new UnreadableContent()
        }));

        var result = await client.SendAsync("group@g.us", "Game reminder");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task SendAsync_WhenSuccessfulResponseIsNotJson_ReturnsSent()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Message delivered")
        }));

        var result = await client.SendAsync("group@g.us", "Game reminder");

        result.Should().Be(PickupPalAnnouncementSendResult.Sent);
    }

    [Fact]
    public async Task SendAsync_WhenTransportFails_ReturnsRetry()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("Provider unavailable"));

        var result = await client.SendAsync("group@g.us", "Game reminder");

        result.Should().Be(PickupPalAnnouncementSendResult.Retry);
    }

    [Fact]
    public async Task SendAsync_WhenHttpClientTimesOut_ReturnsRetry()
    {
        var client = CreateClient((_, _) => throw new TaskCanceledException("Request timed out"));

        var result = await client.SendAsync("group@g.us", "Game reminder");

        result.Should().Be(PickupPalAnnouncementSendResult.Retry);
    }

    [Fact]
    public async Task SendAsync_WhenCallerCancels_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var client = CreateClient((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var action = () => client.SendAsync("group@g.us", "Game reminder", cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static PickupPalAnnouncementClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        PickupPalApiOptions? options = null) =>
        new(new HttpClient(new StubHandler(send)), Options.Create(options ?? new PickupPalApiOptions
        {
            BaseUrl = "https://pickup.example"
        }));

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class UnreadableContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("Provider response content must not be read.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
