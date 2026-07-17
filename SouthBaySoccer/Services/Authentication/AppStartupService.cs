using System.Net;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Services.Clients;

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

            try
            {
                await tokenStore.StoreAsync(tokens);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // The coordinator is already claimed above (TryClaimAuthenticationAsync is the single
                // atomic race point shared with sign-in/callback completion; there is no way back to a
                // re-claimable state from here). If persisting the refreshed tokens fails, still show
                // the authenticated Shell for this session rather than leaving the coordinator claimed
                // with no UI transition — that would wedge the app on the startup screen until relaunch.
                // The refresh itself succeeded, so the in-memory tokens are good for this run; if
                // storage is broken, the next launch will find no refresh token and fall back to sign-in.
            }

            await navigator.ShowAuthenticatedAppAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiRequestException ex) when (IsDefinitiveRejection(ex.StatusCode))
        {
            // The server itself rejected the refresh (e.g. 400 invalid grant) - the refresh token is
            // genuinely dead, so clear it and fall back to the sign-in screen.
            await tokenStore.ClearAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // ApiExceptionHandler passes 401 through unchanged so a rejected refresh token surfaces
            // here as a plain HttpRequestException with StatusCode populated by
            // EnsureSuccessStatusCode - auth/refresh's explicit refusal. Clear the token.
            await tokenStore.ClearAsync();
        }
        catch (HttpRequestException)
        {
            // Connectivity-style failure (offline, DNS, timeout) or a 5xx from the refresh endpoint -
            // not a rejection of the refresh token. Keep the stored tokens; the next launch (or a
            // later in-app refresh) can retry once connectivity returns instead of forcing sign-in.
        }
        catch
        {
            // Non-HTTP failure before the claim point (e.g. malformed refresh response). Stay
            // conservative and keep the tokens rather than signing the user out for an unrelated error.
        }
    }

    /// <summary>
    /// True when the server's response is a definitive 4xx rejection of the refresh request itself
    /// (not a 5xx server error, which is not evidence the refresh token is invalid).
    /// </summary>
    private static bool IsDefinitiveRejection(HttpStatusCode? statusCode) =>
        statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;
}
