using System.Collections.Concurrent;
using SouthBaySoccer.Contracts.Announcements;

namespace SouthBaySoccer.Services.Clients.Caching;

internal sealed class CachedAnnouncementsClient(
    IAnnouncementsClient inner,
    IClientResponseCache cache) : IAnnouncementsClient
{
    internal const string UnreadCountCacheKey = "announcements:unread";
    private static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, Lazy<Task<AnnouncementFeedResponse>>> feedFills =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<UnreadAnnouncementsResponse>>> unreadFills =
        new(StringComparer.Ordinal);
    private readonly object fillGate = new();

    public Task<AnnouncementFeedResponse> GetFeedAsync(
        Guid groupId,
        int limit,
        DateTime? beforeUtc,
        Guid? beforeId,
        CancellationToken cancellationToken)
    {
        if (beforeUtc.HasValue != beforeId.HasValue)
        {
            throw new ArgumentException("Announcement paging requires both beforeUtc and beforeId.");
        }

        if (beforeUtc is not null)
        {
            return inner.GetFeedAsync(groupId, limit, beforeUtc, beforeId, cancellationToken);
        }

        var key = $"announcements:feed:{groupId}:{limit}";
        return GetSingleFlightAsync(
            feedFills,
            key,
            () => cache.GetOrCreateAsync(
                key,
                TimeToLive,
                token => inner.GetFeedAsync(groupId, limit, null, null, token),
                CancellationToken.None),
            cancellationToken);
    }

    public Task<UnreadAnnouncementsResponse> GetUnreadCountAsync(CancellationToken cancellationToken) =>
        GetSingleFlightAsync(
            unreadFills,
            UnreadCountCacheKey,
            () => cache.GetOrCreateAsync(
                UnreadCountCacheKey,
                TimeToLive,
                inner.GetUnreadCountAsync,
                CancellationToken.None),
            cancellationToken);

    public async Task<MarkAnnouncementsReadResponse> MarkReadAsync(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await inner.MarkReadAsync(groupId, cancellationToken);
        }
        finally
        {
            InvalidateAnnouncements();
        }
    }

    public async Task<SentAnnouncementDto> PostAsync(
        Guid groupId,
        PostAnnouncementRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            return await inner.PostAsync(groupId, request, idempotencyKey, cancellationToken);
        }
        finally
        {
            InvalidateAnnouncements();
        }
    }

    public Task<SentAnnouncementsResponse> GetSentAsync(int limit, CancellationToken cancellationToken) =>
        inner.GetSentAsync(limit, cancellationToken);

    private async Task<T> GetSingleFlightAsync<T>(
        ConcurrentDictionary<string, Lazy<Task<T>>> fills,
        string key,
        Func<Task<T>> factory,
        CancellationToken cancellationToken)
    {
        Lazy<Task<T>> lazy;
        Task<T> fill;
        lock (fillGate)
        {
            lazy = fills.GetOrAdd(
                key,
                _ => new Lazy<Task<T>>(factory, LazyThreadSafetyMode.ExecutionAndPublication));
            // Starting the fill under the same gate used by invalidation closes the window where
            // an invalidated lazy could start afterwards and capture the newer cache generation.
            fill = lazy.Value;
        }
        try
        {
            return await fill.WaitAsync(cancellationToken);
        }
        finally
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompleted)
            {
                fills.TryRemove(new KeyValuePair<string, Lazy<Task<T>>>(key, lazy));
            }
        }
    }

    private void InvalidateAnnouncements()
    {
        lock (fillGate)
        {
            cache.Invalidate("announcements:");
            feedFills.Clear();
            unreadFills.Clear();
        }
    }
}
