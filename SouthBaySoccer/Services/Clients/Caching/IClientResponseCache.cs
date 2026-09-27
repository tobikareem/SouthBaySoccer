namespace SouthBaySoccer.Services.Clients.Caching;

/// <summary>
/// Short-lived cache for API read responses. Pull-to-refresh and mutations invalidate through
/// <see cref="Invalidate"/>; sign-out clears everything so one account never sees another's data.
/// </summary>
public interface IClientResponseCache
{
    Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan timeToLive,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>Removes every entry whose key starts with <paramref name="keyPrefix"/>.</summary>
    void Invalidate(string keyPrefix);

    /// <summary>Drops every entry. Used on sign-out and account switch.</summary>
    void Clear();
}
