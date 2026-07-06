using SouthBaySoccer.Configuration;

namespace SouthBaySoccer.Services.Authentication;

public sealed class AppStartupService(
    ISecureTokenStore tokenStore,
    IAuthenticationClient authenticationClient,
    IAuthenticationNavigator navigator,
    ClientDataSourceOptions dataSourceOptions) : IAppStartupService
{
    private const string SeedRefreshToken = "seed-refresh-token";

    public async Task TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
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
