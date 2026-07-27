namespace SouthBaySoccer.Services.Clients;

public interface IAuthenticationSessionRefresher
{
    Task<string?> GetValidAccessTokenAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops any in-memory copy of the access token. Must be called whenever the stored session
    /// changes - sign-out, sign-in, or a restore that clears tokens - because the refresher is a
    /// singleton and would otherwise keep serving the previous session's token.
    /// </summary>
    void InvalidateCachedToken();
}
