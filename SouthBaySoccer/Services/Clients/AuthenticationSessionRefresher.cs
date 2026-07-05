using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Services.Clients;

public sealed class AuthenticationSessionRefresher(
    ISecureTokenStore tokenStore,
    IAuthenticationClient authenticationClient) : IAuthenticationSessionRefresher
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public async Task<string?> GetValidAccessTokenAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh)
        {
            var currentAccessToken = await GetUnexpiredAccessTokenAsync();
            if (!string.IsNullOrWhiteSpace(currentAccessToken))
            {
                return currentAccessToken;
            }
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh)
            {
                var currentAccessToken = await GetUnexpiredAccessTokenAsync();
                if (!string.IsNullOrWhiteSpace(currentAccessToken))
                {
                    return currentAccessToken;
                }
            }

            var refreshToken = await tokenStore.GetRefreshTokenAsync();
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return null;
            }

            try
            {
                var tokens = await authenticationClient.RefreshAsync(refreshToken, cancellationToken);
                await tokenStore.StoreAsync(tokens);
                return tokens.AccessToken;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                await tokenStore.ClearAsync();
                return null;
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private async Task<string?> GetUnexpiredAccessTokenAsync()
    {
        var accessToken = await tokenStore.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var expiresAtUtc = await tokenStore.GetAccessTokenExpiresAtUtcAsync();
        return expiresAtUtc is not null
               && expiresAtUtc.Value.ToUniversalTime() > DateTime.UtcNow.Add(RefreshSkew)
            ? accessToken
            : null;
    }
}
