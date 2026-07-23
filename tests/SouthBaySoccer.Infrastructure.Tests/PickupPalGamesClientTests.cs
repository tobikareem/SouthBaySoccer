using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Infrastructure.Authentication;
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

    private static PickupPalGamesClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(
            new HttpClient(new StubHttpMessageHandler(send)),
            Options.Create(new PickupPalApiOptions { BaseUrl = "https://pickuppal.test" }));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
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
