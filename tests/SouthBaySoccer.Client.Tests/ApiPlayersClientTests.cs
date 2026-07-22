using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public sealed class ApiPlayersClientTests
{
    [Fact]
    public void AddSouthBaySoccerClients_ApiSelected_ResolvesRealPlayersClient()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<ISecureTokenStore>());

        services.AddSouthBaySoccerClients(
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api },
            new PickupPalOptions());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IPlayersClient>()
            .Should().BeOfType<ApiPlayersClient>();
    }

    [Fact]
    public async Task GetDirectoryAsync_SendsDirectoryRouteAndMapsResponse()
    {
        HttpRequestMessage? observed = null;
        var playerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var client = CreateClient(request =>
        {
            observed = request;
            return JsonResponse(
                $$"""
                {
                  "title": "Players",
                  "subtitle": "Search the crew and open career stats.",
                  "totalPlayers": 1,
                  "players": [
                    {
                      "player": {
                        "id": "{{playerId}}",
                        "displayName": "Ada Johnson",
                        "initials": "AJ",
                        "position": "Midfielder",
                        "isGuest": false
                      },
                      "subtitle": "Midfielder \u00B7 #1",
                      "matches": 12
                    }
                  ]
                }
                """);
        });

        var directory = await client.GetDirectoryAsync(CancellationToken.None);

        observed!.Method.Should().Be(HttpMethod.Get);
        observed.RequestUri!.PathAndQuery.Should().Be("/players/directory");
        directory.TotalPlayers.Should().Be(1);
        directory.Players.Should().ContainSingle();
        directory.Players[0].Player.Id.Should().Be(playerId);
        directory.Players[0].Matches.Should().Be(12);
    }

    [Fact]
    public async Task GetDirectoryAsync_WhenResponseIsEmpty_Throws()
    {
        var client = CreateClient(_ => JsonResponse("null"));

        var act = async () => await client.GetDirectoryAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty directory*");
    }

    [Fact]
    public async Task GetDirectoryAsync_ProblemDetailsResponse_ThrowsApiRequestException()
    {
        var client = CreateClient(
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """
                    {
                      "title": "Invalid directory request",
                      "detail": "The directory filter was invalid."
                    }
                    """,
                    System.Text.Encoding.UTF8,
                    "application/problem+json"),
            },
            useApiExceptionHandler: true);

        var act = async () => await client.GetDirectoryAsync(CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ApiRequestException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        exception.Which.Title.Should().Be("Invalid directory request");
        exception.Which.Detail.Should().Be("The directory filter was invalid.");
    }

    [Fact]
    public async Task GetDirectoryAsync_CancellationRequested_PropagatesCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken observedToken = default;
        var client = CreateClientAsync(async (_, cancellationToken) =>
        {
            observedToken = cancellationToken;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The canceled request should not complete.");
        });

        var request = client.GetDirectoryAsync(cancellation.Token);
        cancellation.Cancel();

        var act = async () => await request;

        await act.Should().ThrowAsync<OperationCanceledException>();
        observedToken.CanBeCanceled.Should().BeTrue();
        observedToken.IsCancellationRequested.Should().BeTrue();
    }

    private static ApiPlayersClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> send,
        bool useApiExceptionHandler = false)
    {
        var terminalHandler = new StubHttpMessageHandler(
            (request, _) => Task.FromResult(send(request)));
        HttpMessageHandler handler = useApiExceptionHandler
            ? new ApiExceptionHandler { InnerHandler = terminalHandler }
            : terminalHandler;

        return new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.test/"),
        });
    }

    private static ApiPlayersClient CreateClientAsync(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new HttpClient(new StubHttpMessageHandler(send))
        {
            BaseAddress = new Uri("https://api.test/"),
        });

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
