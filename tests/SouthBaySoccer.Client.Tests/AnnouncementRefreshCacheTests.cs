using FluentAssertions;
using SouthBaySoccer.Contracts.Announcements;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;
using Xunit;

namespace SouthBaySoccer.Client.Tests;

/// <summary>
/// Pull-to-refresh is the only way a player can ask for a fresh answer, since nothing is pushed to
/// the app. These tests pin that the gesture actually reaches the network instead of being answered
/// from the 60-second response cache.
/// </summary>
public sealed class AnnouncementRefreshCacheTests
{
    private static readonly Guid GroupId = Guid.Parse("50000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Refresh_WhenFeedIsCached_StillReachesTheNetwork()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 27, 20, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        var cache = new ClientResponseCache(time);
        var inner = new CountingAnnouncementsClient();
        var model = new AnnouncementsPageModel(
            new CachedAnnouncementsClient(inner, cache),
            new StubNavigator(),
            cache,
            time)
        {
            GroupId = GroupId,
        };

        await model.AppearingCommand.ExecuteAsync(null);
        await model.RefreshCommand.ExecuteAsync(null);

        inner.FeedCalls.Should().Be(
            2,
            because: "the cache TTL has not elapsed, so only an explicit invalidation can make the "
                + "refresh gesture fetch again");
    }

    [Fact]
    public async Task Refresh_WhenListIsAlreadyLoaded_KeepsContentOnScreen()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 27, 20, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        var cache = new ClientResponseCache(time);
        var model = new AnnouncementsPageModel(
            new CachedAnnouncementsClient(new CountingAnnouncementsClient(), cache),
            new StubNavigator(),
            cache,
            time)
        {
            GroupId = GroupId,
        };

        await model.AppearingCommand.ExecuteAsync(null);
        var refresh = model.RefreshCommand.ExecuteAsync(null);

        model.State.Should().NotBe(
            SouthBaySoccer.Controls.ViewState.Loading,
            because: "RefreshView already shows a spinner; blanking the list to a second one makes "
                + "the content flash");
        await refresh;
    }

    [Theory]
    [InlineData(0, "8:00 PM")]      // today — clock only
    [InlineData(2, "Sat 8:00 PM")]  // this week — needs its day back
    [InlineData(30, "Jun 27")]      // older — needs a date
    public async Task TimeLabel_ScalesWithAge(int daysAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 7, 27, 20, 0, 0, TimeSpan.Zero);
        var time = new FixedTimeProvider(now, TimeZoneInfo.Utc);
        var cache = new ClientResponseCache(time);
        var model = new AnnouncementsPageModel(
            new CountingAnnouncementsClient(sentAtUtc: now.UtcDateTime.AddDays(-daysAgo)),
            new StubNavigator(),
            cache,
            time)
        {
            GroupId = GroupId,
        };

        await model.AppearingCommand.ExecuteAsync(null);

        model.DayGroups.SelectMany(group => group).Single().TimeLabel.Should().Be(
            expected,
            because: "a bare clock time on an older announcement claims it arrived today");
    }

    private sealed class StubNavigator : IAnnouncementsNavigator
    {
        public Task GoBackAsync() => Task.CompletedTask;

        public Task GoToAnnouncementsAsync(Guid groupId) => Task.CompletedTask;

        public Task GoToAdminBroadcastAsync() => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => zone;
    }

    private sealed class CountingAnnouncementsClient(DateTime? sentAtUtc = null) : IAnnouncementsClient
    {
        public int FeedCalls { get; private set; }

        public Task<AnnouncementFeedResponse> GetFeedAsync(
            Guid groupId,
            int limit,
            DateTime? beforeUtc,
            Guid? beforeId,
            CancellationToken cancellationToken)
        {
            FeedCalls++;
            return Task.FromResult(new AnnouncementFeedResponse(
                groupId,
                "Saturday crew",
                [
                    new AnnouncementDto(
                        Guid.NewGuid(),
                        groupId,
                        "Saturday crew",
                        "Ayo Okafor",
                        "Pitch change.",
                        sentAtUtc ?? new DateTime(2026, 7, 27, 20, 0, 0, DateTimeKind.Utc),
                        IsUnread: true),
                ],
                UnreadCount: 1,
                NextCursorUtc: null,
                NextCursorId: null));
        }

        public Task<MarkAnnouncementsReadResponse> MarkReadAsync(Guid groupId, CancellationToken cancellationToken) =>
            Task.FromResult(new MarkAnnouncementsReadResponse(groupId, DateTime.UtcNow, 0));

        public Task<UnreadAnnouncementsResponse> GetUnreadCountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new UnreadAnnouncementsResponse(1));

        public Task<SentAnnouncementDto> PostAsync(
            Guid groupId,
            PostAnnouncementRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SentAnnouncementsResponse> GetSentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new SentAnnouncementsResponse([]));
    }
}
