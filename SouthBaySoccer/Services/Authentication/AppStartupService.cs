using SouthBaySoccer.Configuration;

namespace SouthBaySoccer.Services.Authentication;

public sealed class AppStartupService(
    ISecureTokenStore tokenStore,
    IAuthenticationClient authenticationClient,
    IAuthenticationNavigator navigator,
    IAuthenticationCoordinator authenticationCoordinator,
    ClientDataSourceOptions dataSourceOptions) : IAppStartupService
{
    private const string SeedRefreshToken = "seed-refresh-token";

    public async Task TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        if (authenticationCoordinator.IsAuthenticated)
        {
            // Fast path: manual sign-in or an app-link callback already completed before this
            // restore attempt started (either can land while Welcome Back is still showing).
            // Avoids an unnecessary refresh call; the atomic claim below still guards the
            // narrower window after this check.
            return;
        }

        var refreshToken = await tokenStore.GetRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        if (dataSourceOptions.DataSource == ClientDataSource.Api
            && string.Equals(refreshToken, SeedRefreshToken, StringComparison.Ordinal))
        {
            await tokenStore.ClearAsync();
            return;
        }

        try
        {
            var tokens = await authenticationClient.RefreshAsync(refreshToken, cancellationToken);

            if (!await authenticationCoordinator.TryClaimAuthenticationAsync(cancellationToken))
            {
                // The sign-in/callback path atomically claimed completion first — it can land
                // while this refresh call is in flight — and has already shown the authenticated
                // Shell. Do not swap it again.
                return;
            }

            await tokenStore.StoreAsync(tokens);
            await navigator.ShowAuthenticatedAppAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await tokenStore.ClearAsync();
        }
    }
}
