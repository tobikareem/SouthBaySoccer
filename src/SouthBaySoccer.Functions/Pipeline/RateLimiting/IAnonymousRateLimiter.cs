namespace SouthBaySoccer.Functions.Pipeline.RateLimiting;

/// <summary>
/// Sliding-window rate limiting for anonymous endpoints (INV-11). Keys are opaque hashes; callers
/// never pass raw IP addresses, phone numbers, or emails.
/// </summary>
public interface IAnonymousRateLimiter
{
    /// <summary>
    /// Records one hit for <paramref name="key"/> under <paramref name="rule"/> and throws
    /// <see cref="RateLimitExceededException"/> when the window is already full.
    /// </summary>
    /// <param name="rule">The limit and window to apply.</param>
    /// <param name="key">An opaque, non-personal key (for example a hashed IP or phone).</param>
    void EnsureAllowed(RateLimitRule rule, string key);
}

/// <summary>A named limit over a sliding window.</summary>
/// <param name="Scope">Distinguishes rules that may share a key.</param>
/// <param name="Limit">Maximum hits allowed inside the window.</param>
/// <param name="Window">The sliding window length.</param>
public sealed record RateLimitRule(string Scope, int Limit, TimeSpan Window);
