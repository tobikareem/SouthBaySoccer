using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Infrastructure.Authentication;

namespace SouthBaySoccer.Infrastructure.Tests;

public sealed class PickupPalUserClientTests
{
    [Fact]
    public async Task FindByPhoneAsync_WhenUserProfileIncludesSoccerPositions_ReturnsPreferredPositions()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/users/phone/15163447233")
            {
                return JsonResponse(
                    """
                    {
                        "id": "cmhv6brig00dm8i0g9t92otka",
                        "email": "toboibo@yahoo.com",
                        "phoneNumber": "15163447233"
                    }
                    """);
            }

            if (path == "/api/users/cmhv6brig00dm8i0g9t92otka")
            {
                return JsonResponse(
                    """
                    {
                        "id": "cmhv6brig00dm8i0g9t92otka",
                        "email": "toboibo@yahoo.com",
                        "phoneNumber": "15163447233",
                        "firstName": "Tobi",
                        "lastName": "Kareem",
                        "nickName": "Captain",
                        "profilePicture": "https://utfs.io/f/l6kQgaqFZE79BESTrhAXWRp80N4gZH5PKTSEm1fin7vhI2DU",
                        "updatedAt": "2026-06-10T20:22:40.245Z",
                        "userInfo": {
                            "sportsInfo": [
                                {
                                    "sport": "SOCCER",
                                    "positions": [ "st", "rw", "cm" ],
                                    "skillLevel": "ADVANCED",
                                    "isActive": true
                                }
                            ]
                        }
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var httpClient = new HttpClient(handler);
        var client = new PickupPalUserClient(
            httpClient,
            Options.Create(new PickupPalApiOptions { BaseUrl = "https://pickuppal.test" }));

        var user = await client.FindByPhoneAsync("15163447233");

        user.Should().NotBeNull();
        user!.PreferredPositions.Should().Equal("st", "rw", "cm");
    }

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


