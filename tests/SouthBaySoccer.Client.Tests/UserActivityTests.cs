using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.UserActivity;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public sealed class UserActivityTests
{
    [Fact]
    public async Task GetAsync_WhenCalled_UsesOwnerRouteAndMapsRecordedSummary()
    {
        var entry = Entry();
        var client = new ApiUserActivityClient(new HttpClient(new Handler(request =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri.Should().Be(new Uri("https://example.test/api/admin/user-activity?page=2&pageSize=25"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Page([entry], 2, true)) };
        })) { BaseAddress = new Uri("https://example.test/api/") });

        var result = await client.GetAsync(2, 25);

        result.Items.Single().Should().BeEquivalentTo(entry);
        result.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task LoadMore_WhenFailed_RetryKeepsSamePageAndRefreshResetsToFirst()
    {
        var client = new Mock<IUserActivityClient>();
        var first = Entry();
        var second = Entry();
        client.Setup(c => c.GetAsync(1, 25, It.IsAny<CancellationToken>())).ReturnsAsync(Page([first], 1, true));
        client.SetupSequence(c => c.GetAsync(2, 25, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException()).ReturnsAsync(Page([second], 2, false));
        var model = Model(client);

        await model.AppearingCommand.ExecuteAsync(null);
        await model.LoadMoreCommand.ExecuteAsync(null);

        model.Items.Should().ContainSingle();
        model.PagingError.Should().NotBeEmpty();
        model.HasMore.Should().BeTrue();
        await model.LoadMoreCommand.ExecuteAsync(null);
        model.Items.Should().HaveCount(2);
        await model.RefreshCommand.ExecuteAsync(null);
        model.Items.Should().ContainSingle().Which.Id.Should().Be(first.Id);
        client.Verify(c => c.GetAsync(2, 25, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task LoadMore_WhenAccessRevoked_ClearsPrivateRows(HttpStatusCode status)
    {
        var client = new Mock<IUserActivityClient>();
        client.Setup(c => c.GetAsync(1, 25, It.IsAny<CancellationToken>())).ReturnsAsync(Page([Entry()], 1, true));
        client.Setup(c => c.GetAsync(2, 25, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Denied", null, status));
        var model = Model(client);
        await model.AppearingCommand.ExecuteAsync(null);

        await model.LoadMoreCommand.ExecuteAsync(null);

        model.Items.Should().BeEmpty();
        model.HasMore.Should().BeFalse();
        model.StateTitle.Should().Be("Access denied");
        model.State.Should().Be(ViewState.Error);
    }

    [Fact]
    public async Task Refresh_WhenPagingInProgress_DoesNotInterleaveRequests()
    {
        var completion = new TaskCompletionSource<UserActivityPageDto>();
        var client = new Mock<IUserActivityClient>();
        client.Setup(c => c.GetAsync(1, 25, It.IsAny<CancellationToken>())).ReturnsAsync(Page([Entry()], 1, true));
        client.Setup(c => c.GetAsync(2, 25, It.IsAny<CancellationToken>())).Returns(completion.Task);
        var model = Model(client);
        await model.AppearingCommand.ExecuteAsync(null);
        var paging = model.LoadMoreCommand.ExecuteAsync(null);

        await model.RefreshCommand.ExecuteAsync(null);

        client.Verify(c => c.GetAsync(1, 25, It.IsAny<CancellationToken>()), Times.Once);
        completion.SetResult(Page([Entry()], 2, false));
        await paging;
        model.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Refresh_WhenEmpty_ShowsTrackingNotice()
    {
        var client = new Mock<IUserActivityClient>();
        client.Setup(c => c.GetAsync(1, 25, It.IsAny<CancellationToken>())).ReturnsAsync(new UserActivityPageDto([], 1, 25, false, null));
        var model = Model(client);

        await model.RefreshCommand.ExecuteAsync(null);

        model.State.Should().Be(ViewState.Empty);
        model.StateMessage.Should().Contain("Earlier sign-ups and sign-ins are not included");
    }

    [Fact]
    public void Item_WhenMapped_ShowsLocalRecordedTimesAndCurrentGroupStates()
    {
        var entry = Entry();
        var item = new UserActivityItem(entry);

        item.ActivityText.Should().Be("Signed up · " + entry.OccurredAtUtc.ToLocalTime().ToString("g"));
        item.FirstRecordedText.Should().Contain(entry.FirstRecordedActivityAtUtc.ToLocalTime().ToString("g"));
        item.LastRecordedText.Should().Contain(entry.LastRecordedActivityAtUtc.ToLocalTime().ToString("g"));
        item.SignInCountText.Should().Be("Recorded sign-ins: 3");
        item.GroupsText.Should().Be("Current groups: Bay · Approved; Weekend · Pending");
        new UserActivityItem(entry with { ActivityType = "Unknown" }).ActivityText.Should().StartWith("Activity ·");
    }

    private static UserActivityPageModel Model(Mock<IUserActivityClient> client) => new(client.Object, new Mock<IProfileNavigator>().Object);
    private static UserActivityPageDto Page(IReadOnlyList<UserActivityEntryDto> entries, int page, bool hasMore) =>
        new(entries, page, 25, hasMore, new DateTime(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc));
    private static UserActivityEntryDto Entry() => new(Guid.NewGuid(), Guid.NewGuid(), "Alex", "SignUp",
        new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc), 3,
        [new("Bay", "Approved"), new("Weekend", "Pending")]);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
