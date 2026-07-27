using System.Net;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Services.Clients;

public sealed class AuthenticationSessionRefresher(
    ISecureTokenStore tokenStore,
    IAuthenticationClient authenticationClient) : IAuthenticationSessionRefresher
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    // Read-through copy of the stored access token. Secure storage stays the durable record - this
    // only avoids paying two platform-keystore decrypts on every single HTTP request, which is the
    // fixed cost every screen load multiplies. Written under refreshLock or as a same-value cache
    // fill, so a torn read is not possible.
    private volatile CachedAccessToken? cachedAccessToken;

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
                cachedAccessToken = new CachedAccessToken(
                    tokens.AccessToken,
                    tokens.AccessTokenExpiresAtUtc.ToUniversalTime());
                return tokens.AccessToken;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ApiRequestException ex) when (IsDefinitiveRejection(ex.StatusCode))
            {
                // The server itself rejected the refresh (e.g. 400 invalid grant) - the refresh token
                // is genuinely dead, so clear it and force a fresh sign-in.
                cachedAccessToken = null;
                await tokenStore.ClearAsync();
                return null;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                // ApiExceptionHandler passes 401 through unchanged (see its class docs) so a rejected
                // refresh token surfaces here as a plain HttpRequestException with StatusCode populated
                // by EnsureSuccessStatusCode. This is auth/refresh's explicit refusal - clear the token.
                cachedAccessToken = null;
                await tokenStore.ClearAsync();
                return null;
            }
            catch (HttpRequestException)
            {
                // Connectivity-style failure (offline, DNS, timeout) or a 5xx from the refresh endpoint -
                // not a rejection of the refresh token. Keep the stored tokens so the caller can retry
                // once connectivity returns instead of the phone being permanently signed out.
                return null;
            }
            catch
            {
                // Non-HTTP failure (e.g. malformed refresh response). Stay conservative and keep the
                // tokens rather than signing the user out for an unrelated error.
                return null;
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }

    /// <summary>
    /// True when the server's response is a definitive 4xx rejection of the refresh request itself
    /// (not a 5xx server error, which is not evidence the refresh token is invalid).
    /// </summary>
    private static bool IsDefinitiveRejection(HttpStatusCode? statusCode) =>
        statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;

    private async Task<string?> GetUnexpiredAccessTokenAsync()
    {
        if (cachedAccessToken is { } cached)
        {
            return IsUsable(cached.ExpiresAtUtc) ? cached.Token : null;
        }

        var accessToken = await tokenStore.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var expiresAtUtc = await tokenStore.GetAccessTokenExpiresAtUtcAsync();
        if (expiresAtUtc is null)
        {
            return null;
        }

        cachedAccessToken = new CachedAccessToken(accessToken, expiresAtUtc.Value.ToUniversalTime());
        return IsUsable(expiresAtUtc.Value.ToUniversalTime()) ? accessToken : null;
    }

    private static bool IsUsable(DateTime expiresAtUtc) =>
        expiresAtUtc > DateTime.UtcNow.Add(RefreshSkew);

    private sealed record CachedAccessToken(string Token, DateTime ExpiresAtUtc);
}
