using System.Net;
using System.Text.Json;
using FluentAssertions;
using SouthBaySoccer.Contracts.Stats;
using SouthBaySoccer.Services.Clients;
using Xunit;

namespace SouthBaySoccer.Client.Tests;

public sealed class ApiStatsClientTests
{
    private static readonly Guid MatchId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid PlayerId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task GetMatchStatsAsync_WhenServerReturnsStats_RequestsSelfRouteAndMapsTotals()
    {
        HttpRequestMessage? captured = null;
        var client = CreateClient(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """
                {"MatchId":"30000000-0000-0000-0000-000000000001",
                 "CurrentPlayerId":"30000000-0000-0000-0000-000000000002",
                 "MatchSubtitle":"Wed - Google Field","Goals":2,"Assists":1,
                 "IsPendingConfirmation":true,"TeammateSubmissions":[]}
                """);
        });

        var result = await client.GetMatchStatsAsync(MatchId, CancellationToken.None);

        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.PathAndQuery.Should().EndWith($"stats/matches/{MatchId}/me");
        result!.Goals.Should().Be(2);
        result.Assists.Should().Be(1);
        result.IsPendingConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task GetMatchStatsAsync_WhenMatchIsMissing_ReturnsNull()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetMatchStatsAsync(MatchId, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SubmitStatsAsync_WhenCalled_PostsTallyToSubmissionsRouteWithIdempotencyKey()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var client = CreateClient(request =>
        {
            captured = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, """{"MatchId":"30000000-0000-0000-0000-000000000001","AffectedCount":3}""");
        });

        var result = await client.SubmitStatsAsync(MatchId, 2, 1, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.PathAndQuery.Should().EndWith($"stats/matches/{MatchId}/submissions");
        captured.Headers.TryGetValues("Idempotency-Key", out var keys).Should().BeTrue();
        keys!.Single().Should().NotBeNullOrWhiteSpace();
        var payload = Deserialize<SubmitMatchStatsRequest>(body!);
        payload.Goals.Should().Be(2);
        payload.Assists.Should().Be(1);
    }

    [Fact]
    public async Task ConfirmStatsAsync_WhenCalled_PostsToPlayerConfirmRoute()
    {
        HttpRequestMessage? captured = null;
        var client = CreateClient(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"MatchId":"30000000-0000-0000-0000-000000000001","AffectedCount":2}""");
        });

        var result = await client.ConfirmStatsAsync(MatchId, PlayerId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.PathAndQuery
            .Should().EndWith($"stats/matches/{MatchId}/submissions/{PlayerId}/confirm");
    }

    [Fact]
    public async Task GetRateableTeammatesAsync_WhenServerReturnsTeammates_RequestsRouteWithoutRaterId()
    {
        HttpRequestMessage? captured = null;
        var client = CreateClient(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """
                [{"Player":{"Id":"30000000-0000-0000-0000-000000000002","DisplayName":"Bem",
                  "Initials":"B","Position":"","IsGuest":false},
                  "Detail":"2 goals","Rating":0,"IsLiked":false,"IsMvp":false}]
                """);
        });

        var result = await client.GetRateableTeammatesAsync(MatchId, PlayerId, CancellationToken.None);

        captured!.RequestUri!.PathAndQuery.Should().EndWith($"stats/matches/{MatchId}/rateable-teammates");
        // INV-8: the rater is identified by the token, so their id never travels in the request.
        captured.RequestUri.PathAndQuery.Should().NotContain(PlayerId.ToString());
        result.Should().HaveCount(1);
        result[0].Player.DisplayName.Should().Be("Bem");
        result[0].Detail.Should().Be("2 goals");
    }

    [Fact]
    public async Task SubmitRatingsAsync_WhenMvpSelected_SendsRatingsLikesAndMvp()
    {
        string? body = null;
        var client = CreateClient(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, """{"MatchId":"30000000-0000-0000-0000-000000000001","AffectedCount":3}""");
        });

        var result = await client.SubmitRatingsAsync(
            MatchId,
            Guid.NewGuid(),
            [new TeammateRatingDto(PlayerId, 8, IsLiked: true, IsMvp: true)],
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var payload = Deserialize<SubmitPeerFeedbackRequest>(body!);
        payload.Ratings.Single().Score.Should().Be(8);
        payload.Ratings.Single().RatedPlayerProfileId.Should().Be(PlayerId);
        payload.LikedPlayerProfileIds.Single().Should().Be(PlayerId);
        payload.MvpPlayerProfileId.Should().Be(PlayerId);
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static ApiStatsClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new HttpClient(new StubHttpMessageHandler(send))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        });

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
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
