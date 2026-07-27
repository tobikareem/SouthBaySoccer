using System.Net;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Services.Authentication;

public sealed class AuthenticationCoordinator(
    IAuthenticationClient authenticationClient,
    ISecureTokenStore tokenStore,
    IAuthenticationNavigator navigator,
    IClientResponseCache responseCache,
    PickupPalOptions options,
    ClientDataSourceOptions dataSourceOptions) : IAuthenticationCoordinator
{
    public async Task CompleteSignInAsync(
        AuthenticationTokensResponse tokens,
        CancellationToken cancellationToken = default)
    {
        await _completionLock.WaitAsync(cancellationToken);
        try
        {
            if (_completed)
            {
                // Session restore (or the WhatsApp callback) already claimed completion and
                // shown the authenticated Shell while this manual sign-in call was in flight.
                return;
            }

            await tokenStore.StoreAsync(tokens);
            // A different account may have used this device: start from an empty cache.
            responseCache.Clear();
            _completed = true;
            await navigator.ShowAuthenticatedAppAsync(cancellationToken);
        }
        finally
        {
            _completionLock.Release();
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _completionLock.WaitAsync(cancellationToken);
        try
        {
            // Clear the persisted tokens first, then the in-memory flag, so even if navigation fails
            // the app is genuinely signed out (next launch finds no refresh token and shows sign-in).
            await tokenStore.ClearAsync();
            // Cached responses outlive the token, so drop them here: the next account to sign in on
            // this device must never be shown the previous one's data.
            responseCache.Clear();
            _completed = false;
            await navigator.ShowSignInAsync(cancellationToken);
        }
        finally
        {
            _completionLock.Release();
        }
    }

    private readonly SemaphoreSlim _completionLock = new(1, 1);

    // Volatile: read from AppStartupService's restore guard, which may run interleaved with
    // (though not necessarily on a different thread than) the sign-in/callback path.
    private volatile bool _completed;

    public bool IsAuthenticated => _completed;

    /// <summary>
    /// Atomically claims sign-in completion for a caller that persists tokens and shows the
    /// authenticated Shell itself (currently: startup session restore). Shares
    /// <see cref="_completionLock"/> with <see cref="CompleteSignInAsync"/> and
    /// <see cref="CompleteChallengeAsync"/> so exactly one of the three completion paths ever
    /// wins, regardless of the order they land in.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the caller won the race and must proceed with its own token
    /// persistence and Shell navigation; <see langword="false"/> if sign-in already completed via
    /// the manual phone flow or a verified app-link callback, in which case the caller must not
    /// navigate.
    /// </returns>
    public async Task<bool> TryClaimAuthenticationAsync(CancellationToken cancellationToken = default)
    {
        await _completionLock.WaitAsync(cancellationToken);
        try
        {
            if (_completed)
            {
                return false;
            }

            _completed = true;
            return true;
        }
        finally
        {
            _completionLock.Release();
        }
    }

    public Task<bool> TryCompleteChallengeAsync(
        string challengeToken,
        CancellationToken cancellationToken = default) =>
        dataSourceOptions.DataSource == ClientDataSource.Seed
            ? CompleteChallengeAsync(challengeToken, cancellationToken)
            : Task.FromResult(false);

    public async Task<bool> HandleCallbackAsync(
        Uri callbackUri,
        CancellationToken cancellationToken = default)
    {
        if (!MatchesConfiguredCallback(callbackUri) || !TryGetQueryValue(callbackUri, "token", out var token))
        {
            return false;
        }

        return await CompleteChallengeAsync(token, cancellationToken);
    }

    private async Task<bool> CompleteChallengeAsync(
        string challengeToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(challengeToken))
        {
            return false;
        }

        await _completionLock.WaitAsync(cancellationToken);
        try
        {
            if (_completed)
            {
                return true;
            }

            var tokens = await authenticationClient.VerifyWhatsAppChallengeAsync(
                challengeToken,
                cancellationToken);
            await tokenStore.StoreAsync(tokens);
            // Authentication is established once tokens are persisted; record it before the UI swap
            // so a transient navigation hiccup does not force a re-verification on retry.
            _completed = true;
            await navigator.ShowAuthenticatedAppAsync(cancellationToken);
            return true;
        }
        finally
        {
            _completionLock.Release();
        }
    }

    private bool MatchesConfiguredCallback(Uri callbackUri) =>
        string.Equals(callbackUri.Scheme, options.CallbackUri.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(callbackUri.Host, options.CallbackUri.Host, StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            callbackUri.AbsolutePath.TrimEnd('/'),
            options.CallbackUri.AbsolutePath.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    private static bool TryGetQueryValue(Uri uri, string key, out string value)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase))
            {
                value = WebUtility.UrlDecode(parts[1]);
                return !string.IsNullOrWhiteSpace(value);
            }
        }

        value = string.Empty;
        return false;
    }
}
