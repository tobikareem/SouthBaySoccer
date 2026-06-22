using System.Net;
using SouthBaySoccer.Configuration;

namespace SouthBaySoccer.Services.Authentication;

public sealed class AuthenticationCoordinator(
    IAuthenticationClient authenticationClient,
    ISecureTokenStore tokenStore,
    IAuthenticationNavigator navigator,
    PickupPalOptions options,
    ClientDataSourceOptions dataSourceOptions) : IAuthenticationCoordinator
{
    private readonly SemaphoreSlim _completionLock = new(1, 1);
    private bool _completed;

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
