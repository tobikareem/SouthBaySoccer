namespace SouthBaySoccer.Services.Authentication;

public sealed class AppStartupService(
    ISecureTokenStore tokenStore,
    IAuthenticationClient authenticationClient,
    IAuthenticationNavigator navigator) : IAppStartupService
{
    public async Task TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var refreshToken = await tokenStore.GetRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        try
        {
            var tokens = await authenticationClient.RefreshAsync(refreshToken, cancellationToken);
            await tokenStore.StoreAsync(tokens);
            await navigator.ShowAuthenticatedAppAsync();
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
