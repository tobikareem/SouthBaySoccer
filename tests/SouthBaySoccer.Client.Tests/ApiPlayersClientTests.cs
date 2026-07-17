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

    private static ApiPlayersClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new HttpClient(new StubHttpMessageHandler(send))
        {
            BaseAddress = new Uri("https://api.test/"),
        });

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
