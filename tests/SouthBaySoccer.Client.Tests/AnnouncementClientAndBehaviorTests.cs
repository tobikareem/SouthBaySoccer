using System.Net;
using System.Text;
using FluentAssertions;
using SouthBaySoccer.Contracts.Announcements;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

public sealed class AnnouncementClientAndBehaviorTests
{
    private static readonly Guid GroupId = Guid.Parse("50000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task GetFeedAsync_WithCompoundCursor_SendsBothCursorValues()
    {
        HttpRequestMessage? observed = null;
        var beforeUtc = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
        var beforeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var client = new ApiAnnouncementsClient(CreateHttpClient(request =>
        {
            observed = request;
            return JsonResponse(
                $$"""{"groupId":"{{GroupId}}","groupName":"Saturday crew","announcements":[],"unreadCount":0,"nextCursorUtc":null,"nextCursorId":null}""");
        }));

        await client.GetFeedAsync(GroupId, 20, beforeUtc, beforeId, CancellationToken.None);

        observed!.RequestUri!.PathAndQuery.Should()
            .Be($"/groups/{GroupId}/announcements?limit=20&before=2026-07-27T12%3A00%3A00.0000000Z&beforeId={beforeId:D}");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task GetFeedAsync_WithHalfCursor_RejectsRequest(bool hasTime, bool hasId)
    {
        var client = new ApiAnnouncementsClient(CreateHttpClient(_ => throw new InvalidOperationException()));

        var act = async () => await client.GetFeedAsync(
            GroupId,
            20,
            hasTime ? DateTime.UtcNow : null,
            hasId ? Guid.NewGuid() : null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PostAsync_SendsBodyAndCallerStableIdempotencyKey()
    {
        string? observedKey = null;
        string? observedBody = null;
        var key = Guid.NewGuid().ToString("N");
        var client = new ApiAnnouncementsClient(CreateHttpClient(request =>
        {
            observedKey = request.Headers.GetValues("Idempotency-Key").Single();
            observedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(
                $$"""{"id":"{{Guid.NewGuid()}}","groupId":"{{GroupId}}","groupName":"Saturday crew","body":"Field moved.","sentAtUtc":"2026-07-27T12:00:00Z","readCount":0,"recipientCount":24}""");
        }));

        await client.PostAsync(
            GroupId,
            new PostAnnouncementRequest("Field moved.", true),
            key,
            CancellationToken.None);

        observedKey.Should().Be(key);
        observedBody.Should().Contain("\"body\":\"Field moved.\"").And.Contain("\"sendPush\":true");
    }

    [Fact]
    public async Task LoadMore_WithCompoundCursor_AppendsRatherThanReplaces()
    {
        var sentAt = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
        var firstId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var secondId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var client = new QueueAnnouncementsClient(
            new AnnouncementFeedResponse(
                GroupId,
                "Saturday crew",
                [Dto(firstId, sentAt, true)],
                1,
                sentAt,
                firstId),
            new AnnouncementFeedResponse(
                GroupId,
                "Saturday crew",
                [Dto(secondId, sentAt, false)],
                1,
                null,
                null));
        var model = CreateFeedModel(client);

        await model.AppearingCommand.ExecuteAsync(null);
        await model.LoadMoreCommand.ExecuteAsync(null);

        model.DayGroups.SelectMany(group => group).Select(item => item.Id)
            .Should().Equal(firstId, secondId);
        client.Cursors.Should().ContainSingle()
            .Which.Should().Be((sentAt, firstId));
    }

    [Fact]
    public async Task Appearing_AcrossPacificLocalMidnight_GroupsByLocalDate()
    {
        var time = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"));
        var client = new QueueAnnouncementsClient(new AnnouncementFeedResponse(
            GroupId,
            "Saturday crew",
            [
                Dto(Guid.NewGuid(), new DateTime(2026, 7, 27, 8, 30, 0, DateTimeKind.Utc), false),
                Dto(Guid.NewGuid(), new DateTime(2026, 7, 27, 6, 30, 0, DateTimeKind.Utc), false)
            ],
            0,
            null,
            null));
        var model = new AnnouncementsPageModel(client, new StubNavigator(), new ClientResponseCache(time), time) { GroupId = GroupId };

        await model.AppearingCommand.ExecuteAsync(null);

        model.DayGroups.Select(group => group.Name).Should().Equal("Today", "Earlier");
        model.DayGroups.Should().OnlyContain(group => group.Count == 1);
    }

    [Fact]
    public async Task Send_RetrySameComposition_ReusesKey_ButPushChangeMintsNewKey()
    {
        var client = new RecordingPostClient(failuresBeforeSuccess: 2);
        var model = CreateComposer(client);
        await model.AppearingCommand.ExecuteAsync(null);
        model.Body = "Field moved to Marina.";

        await model.SendCommand.ExecuteAsync(null);
        await model.SendCommand.ExecuteAsync(null);
        model.SendPush = !model.SendPush;
        await model.SendCommand.ExecuteAsync(null);

        client.Keys.Should().HaveCount(3);
        client.Keys[1].Should().Be(client.Keys[0]);
        client.Keys[2].Should().NotBe(client.Keys[0]);
    }

    [Fact]
    public async Task Send_GroupChangeAfterFailure_MintsNewKey()
    {
        var client = new RecordingPostClient(failuresBeforeSuccess: 1);
        var model = CreateComposer(client);
        await model.AppearingCommand.ExecuteAsync(null);
        model.Body = "Field moved.";

        await model.SendCommand.ExecuteAsync(null);
        model.SelectGroupCommand.Execute(model.Groups[1]);
        await model.SendCommand.ExecuteAsync(null);

        client.Keys.Should().HaveCount(2);
        client.Keys[1].Should().NotBe(client.Keys[0]);
    }

    [Fact]
    public async Task Send_BodyChangeAfterFailure_MintsNewKey()
    {
        var client = new RecordingPostClient(failuresBeforeSuccess: 1);
        var model = CreateComposer(client);
        await model.AppearingCommand.ExecuteAsync(null);
        model.Body = "Field moved.";

        await model.SendCommand.ExecuteAsync(null);
        model.Body = "Field moved to Marina.";
        await model.SendCommand.ExecuteAsync(null);

        client.Keys.Should().HaveCount(2);
        client.Keys[1].Should().NotBe(client.Keys[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Send_EmptyOrWhitespace_CannotExecute(string body)
    {
        var model = CreateComposer(new RecordingPostClient());
        await model.AppearingCommand.ExecuteAsync(null);

        model.Body = body;

        model.SendCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Send_AtFiveHundredCharacters_ExecutesAndDisablesWhileInFlight()
    {
        var client = new RecordingPostClient(blockPost: true);
        var model = CreateComposer(client);
        await model.AppearingCommand.ExecuteAsync(null);
        model.Body = new string('x', AdminBroadcastPageModel.MaximumBodyLength);

        var send = model.SendCommand.ExecuteAsync(null);
        await client.PostStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        model.IsSending.Should().BeTrue();
        model.SendCommand.CanExecute(null).Should().BeFalse();
        client.ReleasePost();
        await send;
    }

    [Fact]
    public async Task CachedUnreadCount_SecondAndConcurrentCalls_UseOneInnerRequest()
    {
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        var inner = new RecordingPostClient(blockUnread: true);
        var cached = new CachedAnnouncementsClient(inner, new ClientResponseCache(time));

        var first = cached.GetUnreadCountAsync(CancellationToken.None);
        await inner.UnreadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = cached.GetUnreadCountAsync(CancellationToken.None);
        inner.ReleaseUnread();
        await Task.WhenAll(first, second);
        await cached.GetUnreadCountAsync(CancellationToken.None);

        inner.UnreadCalls.Should().Be(1);
    }

    [Fact]
    public async Task MarkAllRead_WhenAlreadyZero_DoesNotIssueRequest()
    {
        var client = new QueueAnnouncementsClient(new AnnouncementFeedResponse(
            GroupId, "Saturday crew", [Dto(Guid.NewGuid(), DateTime.UtcNow, false)], 0, null, null));
        var model = CreateFeedModel(client);
        await model.AppearingCommand.ExecuteAsync(null);

        await model.MarkAllReadCommand.ExecuteAsync(null);

        client.MarkReadCalls.Should().Be(0);
    }

    [Fact]
    public async Task LoadMore_WhenRequestFails_PreservesLoadedContentAndShowsIncrementalError()
    {
        var first = Dto(Guid.NewGuid(), new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc), false);
        var client = new FailingSecondPageClient(new AnnouncementFeedResponse(
            GroupId, "Saturday crew", [first], 0, first.SentAtUtc, first.Id));
        var model = CreateFeedModel(client);
        await model.AppearingCommand.ExecuteAsync(null);

        await model.LoadMoreCommand.ExecuteAsync(null);

        model.State.Should().Be(SouthBaySoccer.Controls.ViewState.Content);
        model.DayGroups.SelectMany(group => group).Should().ContainSingle(item => item.Id == first.Id);
        model.HasLoadMoreError.Should().BeTrue();
    }

    private static AnnouncementsPageModel CreateFeedModel(IAnnouncementsClient client)
    {
        var time = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 27, 20, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.Utc);

        return new AnnouncementsPageModel(client, new StubNavigator(), new ClientResponseCache(time), time)
        {
            GroupId = GroupId
        };
    }

    private static AdminBroadcastPageModel CreateComposer(IAnnouncementsClient client) =>
        new(
            new SeedGroupsClient(),
            client,
            new StubNavigator(),
            new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 27, 20, 0, 0, TimeSpan.Zero),
                TimeZoneInfo.Utc));

    private static AnnouncementDto Dto(Guid id, DateTime sentAtUtc, bool unread) =>
        new(id, GroupId, "Saturday crew", "Admin", $"Message {id:N}", sentAtUtc, unread);

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new StubHttpMessageHandler(send)) { BaseAddress = new Uri("https://api.test/") };

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class QueueAnnouncementsClient(params AnnouncementFeedResponse[] pages) : IAnnouncementsClient
    {
        private readonly Queue<AnnouncementFeedResponse> pages = new(pages);
        public List<(DateTime? BeforeUtc, Guid? BeforeId)> Cursors { get; } = [];
        public int MarkReadCalls { get; private set; }

        public Task<AnnouncementFeedResponse> GetFeedAsync(Guid groupId, int limit, DateTime? beforeUtc, Guid? beforeId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (beforeUtc is not null)
            {
                Cursors.Add((beforeUtc, beforeId));
            }
            return Task.FromResult(pages.Dequeue());
        }

        public Task<MarkAnnouncementsReadResponse> MarkReadAsync(Guid groupId, CancellationToken cancellationToken)
        {
            MarkReadCalls++;
            return Task.FromResult(new MarkAnnouncementsReadResponse(groupId, DateTime.UtcNow, 0));
        }

        public Task<UnreadAnnouncementsResponse> GetUnreadCountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new UnreadAnnouncementsResponse(0));

        public Task<SentAnnouncementDto> PostAsync(Guid groupId, PostAnnouncementRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SentAnnouncementsResponse> GetSentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new SentAnnouncementsResponse([]));
    }

    private sealed class RecordingPostClient(
        int failuresBeforeSuccess = 0,
        bool blockPost = false,
        bool blockUnread = false) : IAnnouncementsClient
    {
        private readonly TaskCompletionSource postRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource unreadRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int attempts;
        public List<string> Keys { get; } = [];
        public TaskCompletionSource PostStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource UnreadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int UnreadCalls { get; private set; }

        public void ReleasePost() => postRelease.TrySetResult();
        public void ReleaseUnread() => unreadRelease.TrySetResult();

        public Task<AnnouncementFeedResponse> GetFeedAsync(Guid groupId, int limit, DateTime? beforeUtc, Guid? beforeId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MarkAnnouncementsReadResponse> MarkReadAsync(Guid groupId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<UnreadAnnouncementsResponse> GetUnreadCountAsync(CancellationToken cancellationToken)
        {
            UnreadCalls++;
            UnreadStarted.TrySetResult();
            if (blockUnread)
            {
                await unreadRelease.Task.WaitAsync(cancellationToken);
            }
            return new UnreadAnnouncementsResponse(3);
        }

        public async Task<SentAnnouncementDto> PostAsync(Guid groupId, PostAnnouncementRequest request, string idempotencyKey, CancellationToken cancellationToken)
        {
            Keys.Add(idempotencyKey);
            attempts++;
            PostStarted.TrySetResult();
            if (blockPost)
            {
                await postRelease.Task.WaitAsync(cancellationToken);
            }
            if (attempts <= failuresBeforeSuccess)
            {
                throw new HttpRequestException("offline");
            }
            return new SentAnnouncementDto(Guid.NewGuid(), groupId, "Saturday crew", request.Body, DateTime.UtcNow, 0, 24);
        }

        public Task<SentAnnouncementsResponse> GetSentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new SentAnnouncementsResponse([]));
    }

    private sealed class FailingSecondPageClient(AnnouncementFeedResponse firstPage) : IAnnouncementsClient
    {
        private int calls;

        public Task<AnnouncementFeedResponse> GetFeedAsync(Guid groupId, int limit, DateTime? beforeUtc, Guid? beforeId, CancellationToken cancellationToken)
        {
            calls++;
            return calls == 1
                ? Task.FromResult(firstPage)
                : Task.FromException<AnnouncementFeedResponse>(new HttpRequestException("offline"));
        }

        public Task<MarkAnnouncementsReadResponse> MarkReadAsync(Guid groupId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<UnreadAnnouncementsResponse> GetUnreadCountAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<SentAnnouncementDto> PostAsync(Guid groupId, PostAnnouncementRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<SentAnnouncementsResponse> GetSentAsync(int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubNavigator : IAnnouncementsNavigator
    {
        public Task GoToAnnouncementsAsync(Guid groupId) => Task.CompletedTask;
        public Task GoToAdminBroadcastAsync() => Task.CompletedTask;
        public Task GoBackAsync() => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => zone;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
