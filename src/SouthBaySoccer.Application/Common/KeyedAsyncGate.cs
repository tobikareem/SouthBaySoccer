namespace SouthBaySoccer.Application.Common;

/// <summary>
/// In-process, per-key async lock. Registered as a singleton by the composition root; the scope is
/// one Function instance, so it serializes work on a key within a process only (cross-instance
/// ordering is a documented limit of the callers). Entries are created on demand and removed once
/// the last holder releases, so the dictionary never grows with the key space.
/// </summary>
/// <typeparam name="TKey">The lock key.</typeparam>
public abstract class KeyedAsyncGate<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> entries = new();
    private readonly object sync = new();

    /// <summary>Waits for exclusive access to the key; dispose the lease to release it.</summary>
    public async Task<IDisposable> AcquireAsync(TKey key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (sync)
        {
            if (!entries.TryGetValue(key, out var existing))
            {
                existing = new Entry();
                entries[key] = existing;
            }

            entry = existing;
            entry.Holders++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Release(key, entry, acquired: false);
            throw;
        }

        return new Lease(this, key, entry);
    }

    private void Release(TKey key, Entry entry, bool acquired)
    {
        if (acquired)
        {
            entry.Semaphore.Release();
        }

        lock (sync)
        {
            entry.Holders--;
            if (entry.Holders == 0)
            {
                entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Holders { get; set; }
    }

    private sealed class Lease(KeyedAsyncGate<TKey> gate, TKey key, Entry entry) : IDisposable
    {
        private bool released;

        public void Dispose()
        {
            if (released)
            {
                return;
            }

            released = true;
            gate.Release(key, entry, acquired: true);
        }
    }
}
