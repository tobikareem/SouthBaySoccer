using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Infrastructure.Authentication;
using SouthBaySoccer.Infrastructure.Groups;
using SouthBaySoccer.Infrastructure.Scheduling;

namespace SouthBaySoccer.Infrastructure.Tests;

public sealed class PickupPalGamesClientTests
{
    // Trimmed from a real /api/games/active response; deliberately keeps the phone-bearing
    // whatsappJid/groupId fields so the tests prove they never survive deserialization.
    private const string ActiveGamesJson =
        """
        {
          "games": [
            {
              "id": "cmrti8zc400fh75unavs2vrgi",
              "groupId": "14082428927-1520565400@g.us",
              "date": "2026-07-23",
              "time": "09:30 pm",
              "location": "969 e caribbean dr, sunnyvale, ca 94089",
              "maxPlayers": 10,
              "dateTime": "2026-07-24T04:30:00.000Z",
              "creatorId": "217973935587425@lid",
              "status": "active",
              "gameType": "WHATSAPP_GROUP",
              "sport": "SOCCER",
              "participants": [
                {
                  "id": "cmrti951l00fm75un3agopbyl",
                  "userId": "cmrmarkuser0001",
                  "whatsappJid": "217973935587425@lid",
                  "phoneNumber": "+1 (408) 555-1234",
                  "displayName": "Mark A",
                  "isGuest": false,
                  "joinedAt": "2026-07-20T17:35:11.817Z",
                  "isWaitlist": false
                },
                {
                  "id": "cmrtlb5w700ga75unour9mqzv",
                  "whatsappJid": null,
                  "displayName": "tope",
                  "isGuest": true,
                  "addedByWhatsappJid": "21424001576962@lid",
                  "joinedAt": "2026-07-20T19:00:45.079Z",
                  "isWaitlist": true
                }
              ],
              "group": {
                "id": "14082428927-1520565400@g.us",
                "subscriberId": "14082428927@c.us",
                "groupName": "Fire FC",
                "timezone": "America/Los_Angeles"
              }
            }
          ]
        }
        """;

    private const string SingleGameJson =
        """
        {
          "id": "cmrti8zc400fh75unavs2vrgi",
          "groupId": "14082428927-1520565400@g.us",
          "location": "969 e caribbean dr, sunnyvale, ca 94089",
          "maxPlayers": 10,
          "dateTime": "2026-07-24T04:30:00.000Z",
          "status": "active",
          "participants": [
            {
              "id": "cmrti951l00fm75un3agopbyl",
              "userId": "cmrmarkuser0001",
              "whatsappJid": "217973935587425@lid",
              "displayName": "Mark A",
              "isGuest": false,
              "joinedAt": "2026-07-20T17:35:11.817Z",
              "isWaitlist": false
            }
          ],
          "group": { "groupName": "Fire FC" }
        }
        """;

    [Fact]
    public async Task GetActiveGamesAsync_ParsesGamesAndSanitizesParticipants()
    {
        HttpRequestMessage? observed = null;
        var client = CreateClient(request =>
        {
            observed = request;
            return JsonResponse(ActiveGamesJson);
        });

        var games = await client.GetActiveGamesAsync();

        observed!.RequestUri!.AbsolutePath.Should().Be("/api/games/active");
        var game = games.Should().ContainSingle().Subject;
        game.Id.Should().Be("cmrti8zc400fh75unavs2vrgi");
        game.StartsAtUtc.Should().Be(new DateTime(2026, 7, 24, 4, 30, 0, DateTimeKind.Utc));
        game.StartsAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        game.Location.Should().Be("969 e caribbean dr, sunnyvale, ca 94089");
        game.MaxPlayers.Should().Be(10);
        game.Status.Should().Be("active");
        game.GroupName.Should().Be("Fire FC");
        game.Participants.Should().HaveCount(2);
        game.Participants[0].DisplayName.Should().Be("Mark A");
        game.Participants[0].IsWaitlist.Should().BeFalse();
        game.Participants[1].DisplayName.Should().Be("tope");
        game.Participants[1].IsGuest.Should().BeTrue();
        game.Participants[1].IsWaitlist.Should().BeTrue();
    }

    [Fact]
    public async Task GetActiveGamesAsync_HashesPhoneAndWhatsAppIdentityInsteadOfExposingRawValues()
    {
        var client = CreateClient(_ => JsonResponse(ActiveGamesJson));

        var games = await client.GetActiveGamesAsync();

        var mark = games.Single().Participants[0];
        mark.UserId.Should().Be("cmrmarkuser0001");
        mark.PhoneNumberHash.Should().MatchRegex("^[0-9A-F]{64}$", "phones cross the boundary only as SHA-256 hashes");
        mark.MaskedPhoneNumber.Should().Be("+******1234");
        mark.WhatsAppJidHash.Should().MatchRegex("^[0-9A-F]{64}$").And.NotContain("@lid");

        var tope = games.Single().Participants[1];
        tope.UserId.Should().BeNull();
        tope.PhoneNumberHash.Should().BeNull();
        tope.WhatsAppJidHash.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveGamesAsync_SanitizedShapeNeverCarriesWhatsAppIdentifiers()
    {
        var client = CreateClient(_ => JsonResponse(ActiveGamesJson));

        var games = await client.GetActiveGamesAsync();

        // The persisted snapshot shape must exclude the identity fields entirely: no raw JIDs, no
        // phone digits, and not even the hashes (they live on PlayerProfile, not in snapshots).
        var serialized = System.Text.Json.JsonSerializer.Serialize(games);
        serialized.Should().NotContain("@lid").And.NotContain("@g.us").And.NotContain("@c.us");
        serialized.Should().NotContain("4085551234").And.NotContain("Hash").And.NotContain("cmrmarkuser0001");
    }

    [Fact]
    public async Task GetActiveGamesAsync_WhenEndpointNotFound_ReturnsEmpty()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var games = await client.GetActiveGamesAsync();

        games.Should().BeEmpty();
    }

    // ---- RSVP-9: single game, roster add/remove, API key, error shapes ----

    [Fact]
    public async Task GetGameAsync_WhenGameExists_ParsesTheSingleGameShape()
    {
        HttpRequestMessage? observed = null;
        var client = CreateClient(request =>
        {
            observed = request;
            return JsonResponse(SingleGameJson);
        });

        var game = await client.GetGameAsync("cmrti8zc400fh75unavs2vrgi");

        observed!.Method.Should().Be(HttpMethod.Get);
        observed.RequestUri!.AbsolutePath.Should().Be("/api/games/cmrti8zc400fh75unavs2vrgi");
        game.Should().NotBeNull();
        game!.Id.Should().Be("cmrti8zc400fh75unavs2vrgi");
        game.Participants.Should().ContainSingle().Which.UserId.Should().Be("cmrmarkuser0001");
    }

    [Fact]
    public async Task GetGameAsync_WhenWrappedInGameEnvelope_StillParses()
    {
        var client = CreateClient(_ => JsonResponse($$"""{ "game": {{SingleGameJson}} }"""));

        var game = await client.GetGameAsync("cmrti8zc400fh75unavs2vrgi");

        game.Should().NotBeNull();
        game!.MaxPlayers.Should().Be(10);
    }

    [Fact]
    public async Task GetGameAsync_WhenGameIsGone_ReturnsNull()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var game = await client.GetGameAsync("gone");

        game.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetGameAsync_WhenServerOrAuthFails_ThrowsUnavailable(HttpStatusCode statusCode)
    {
        var client = CreateClient(_ => new HttpResponseMessage(statusCode));

        var act = () => client.GetGameAsync("game-1");

        await act.Should().ThrowAsync<ApplicationServiceUnavailableException>();
    }

    [Fact]
    public async Task AddPlayerAsync_WhenAccepted_PostsIdAndNameOnlyAndReturnsApplied()
    {
        HttpRequestMessage? observed = null;
        string? body = null;
        var client = CreateClient(request =>
        {
            observed = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{ "id": "participant-1" }""", HttpStatusCode.Created);
        });

        var result = await client.AddPlayerAsync("game-1", "pp-user-1", "Ada Lovelace");

        result.Should().Be(PickupPalRosterPushResult.Applied);
        observed!.Method.Should().Be(HttpMethod.Post);
        observed.RequestUri!.AbsolutePath.Should().Be("/api/games/game-1/players");
        body.Should().Contain("\"playerId\":\"pp-user-1\"").And.Contain("\"playerName\":\"Ada Lovelace\"");
        body.Should().NotContain("playerNumber", "a phone number must never be put on the wire");
    }

    [Theory]
    [InlineData("""{ "error": "Player already in game" }""", HttpStatusCode.BadRequest, PickupPalRosterPushResult.AlreadyApplied)]
    [InlineData("""{ "error": "Player is already on the waitlist" }""", HttpStatusCode.Conflict, PickupPalRosterPushResult.AlreadyApplied)]
    [InlineData("""{ "error": "Game is full" }""", HttpStatusCode.BadRequest, PickupPalRosterPushResult.GameFull)]
    [InlineData("""{ "error": { "message": "Game is full", "status": 400 } }""", HttpStatusCode.BadRequest, PickupPalRosterPushResult.GameFull)]
    [InlineData("""{ "error": "Game not found" }""", HttpStatusCode.NotFound, PickupPalRosterPushResult.GameNotFound)]
    [InlineData("""{ "error": "Game not found" }""", HttpStatusCode.BadRequest, PickupPalRosterPushResult.GameNotFound)]
    [InlineData("", HttpStatusCode.NotFound, PickupPalRosterPushResult.GameNotFound)]
    [InlineData("""{ "error": "User not found" }""", HttpStatusCode.NotFound, PickupPalRosterPushResult.Rejected)]
    [InlineData("""{ "error": "Missing required fields" }""", HttpStatusCode.BadRequest, PickupPalRosterPushResult.Rejected)]
    [InlineData("not json", HttpStatusCode.BadRequest, PickupPalRosterPushResult.Rejected)]
    public async Task AddPlayerAsync_WhenRejected_ClassifiesBothErrorShapes(
        string body,
        HttpStatusCode statusCode,
        PickupPalRosterPushResult expected)
    {
        var client = CreateClient(_ => JsonResponse(body, statusCode));

        var result = await client.AddPlayerAsync("game-1", "pp-user-1", "Ada");

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AddPlayerAsync_WhenServerOrAuthFails_ThrowsUnavailable(HttpStatusCode statusCode)
    {
        var client = CreateClient(_ => JsonResponse("""{ "error": { "message": "Internal Server Error", "status": 500 } }""", statusCode));

        var act = () => client.AddPlayerAsync("game-1", "pp-user-1", "Ada");

        await act.Should().ThrowAsync<ApplicationServiceUnavailableException>();
    }

    [Fact]
    public async Task AddPlayerAsync_WhenNetworkFails_ThrowsUnavailable()
    {
        var client = CreateClient(_ => throw new HttpRequestException("connection refused"));

        var act = () => client.AddPlayerAsync("game-1", "pp-user-1", "Ada");

        await act.Should().ThrowAsync<ApplicationServiceUnavailableException>();
    }

    [Fact]
    public async Task AddPlayerAsync_WhenTimedOut_ThrowsUnavailable()
    {
        var client = CreateClient(_ => throw new TaskCanceledException("timeout"));

        var act = () => client.AddPlayerAsync("game-1", "pp-user-1", "Ada");

        await act.Should().ThrowAsync<ApplicationServiceUnavailableException>();
    }

    [Fact]
    public async Task RemovePlayerAsync_WhenAccepted_DeletesByPlayerIdAndReturnsApplied()
    {
        HttpRequestMessage? observed = null;
        var client = CreateClient(request =>
        {
            observed = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var result = await client.RemovePlayerAsync("game-1", "pp-user-1");

        result.Should().Be(PickupPalRosterPushResult.Applied);
        observed!.Method.Should().Be(HttpMethod.Delete);
        observed.RequestUri!.AbsolutePath.Should().Be("/api/games/game-1/players/pp-user-1");
    }

    [Theory]
    [InlineData("", HttpStatusCode.NotFound, PickupPalRosterPushResult.AlreadyApplied)]
    [InlineData("""{ "error": "Player not found in game" }""", HttpStatusCode.NotFound, PickupPalRosterPushResult.AlreadyApplied)]
    [InlineData("""{ "error": "Player is not in this game" }""", HttpStatusCode.BadRequest, PickupPalRosterPushResult.AlreadyApplied)]
    [InlineData("""{ "error": "User not found" }""", HttpStatusCode.BadRequest, PickupPalRosterPushResult.AlreadyApplied)]
    [InlineData("""{ "error": "Game not found" }""", HttpStatusCode.NotFound, PickupPalRosterPushResult.GameNotFound)]
    [InlineData("""{ "error": { "message": "Game not found", "status": 404 } }""", HttpStatusCode.NotFound, PickupPalRosterPushResult.GameNotFound)]
    [InlineData("""{ "error": "Cannot remove the creator" }""", HttpStatusCode.BadRequest, PickupPalRosterPushResult.Rejected)]
    public async Task RemovePlayerAsync_WhenRejected_ClassifiesBothErrorShapes(
        string body,
        HttpStatusCode statusCode,
        PickupPalRosterPushResult expected)
    {
        var client = CreateClient(_ => JsonResponse(body, statusCode));

        var result = await client.RemovePlayerAsync("game-1", "pp-user-1");

        result.Should().Be(expected);
    }

    [Fact]
    public async Task RemovePlayerAsync_WhenServerFails_ThrowsUnavailable()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var act = () => client.RemovePlayerAsync("game-1", "pp-user-1");

        await act.Should().ThrowAsync<ApplicationServiceUnavailableException>();
    }

    [Fact]
    public async Task SendAsync_WhenApiKeyConfigured_SendsItInTheConfiguredHeaderOnEveryCall()
    {
        var observed = new List<HttpRequestMessage>();
        var client = CreateClient(
            request =>
            {
                observed.Add(request);
                return request.Method == HttpMethod.Get
                    ? JsonResponse(ActiveGamesJson)
                    : new HttpResponseMessage(HttpStatusCode.OK);
            },
            options =>
            {
                options.ApiKey = "secret-key";
                options.ApiKeyHeaderName = "X-Bot-Key";
            });

        await client.GetActiveGamesAsync();
        await client.AddPlayerAsync("game-1", "pp-user-1", "Ada");
        await client.RemovePlayerAsync("game-1", "pp-user-1");

        observed.Should().HaveCount(3);
        observed.Should().OnlyContain(request =>
            request.Headers.Contains("X-Bot-Key")
            && request.Headers.GetValues("X-Bot-Key").Single() == "secret-key");
    }

    [Fact]
    public async Task SendAsync_WhenApiKeyMissing_SendsNoApiKeyHeader()
    {
        HttpRequestMessage? observed = null;
        var client = CreateClient(request =>
        {
            observed = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.AddPlayerAsync("game-1", "pp-user-1", "Ada");

        observed!.Headers.Contains("X-Api-Key").Should().BeFalse();
    }

    [Fact]
    public void PickupPalGamesClient_WhenConstructed_TakesNoLoggerSoRequestUrisAreNeverLogged()
    {
        // Game and player ids travel in request URIs; the URI-logging ban is enforced structurally
        // the same way as for the user and onboarding clients: no ILogger can reach this type.
        var constructorParameters = typeof(PickupPalGamesClient)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        constructorParameters.Should().NotContain(type =>
            typeof(ILogger).IsAssignableFrom(type) || typeof(ILoggerFactory).IsAssignableFrom(type));
        typeof(PickupPalGamesClient).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Should().NotContain("Microsoft.Extensions.Logging");
    }

    [Theory]
    [InlineData(nameof(IPickupPalGamesClient))]
    [InlineData(nameof(IPickupPalUserClient))]
    [InlineData(nameof(IPickupPalOnboardingClient))]
    [InlineData(nameof(PickupPalGroupClient))]
    public void AddInfrastructure_WhenPickupPalClientRegistered_BuildsAPipelineWithNoLoggingOrCustomHandlers(string clientName)
    {
        // The factory's default LoggingHttpMessageHandler/LoggingScopeHttpMessageHandler log the
        // outbound request URI at Information; every Pickup Pal client carries phone digits,
        // tokens, emails, or ids in its URIs, so the built pipeline must contain neither them nor
        // any custom DelegatingHandler.
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.AddInfrastructure("Server=localhost;Database=Test;Trusted_Connection=True;");
        using var provider = services.BuildServiceProvider();
        var factoryOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(clientName);
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);

        factoryOptions.HttpMessageHandlerBuilderActions.Should().BeEmpty(
            "a DelegatingHandler on this client could log the request URI");
        var chain = new List<string>();
        for (var current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            chain.Add(current.GetType().Name);
        }

        chain.Should().NotContain(name => name.Contains("Logging", StringComparison.Ordinal));
        chain.Should().HaveCount(2, "only the factory's lifetime tracker and the primary handler may remain: {0}", string.Join(" -> ", chain));
    }

    private static PickupPalGamesClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> send,
        Action<PickupPalApiOptions>? configure = null)
    {
        var options = new PickupPalApiOptions { BaseUrl = "https://pickuppal.test" };
        configure?.Invoke(options);
        return new PickupPalGamesClient(new HttpClient(new StubHttpMessageHandler(send)), Options.Create(options));
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
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
