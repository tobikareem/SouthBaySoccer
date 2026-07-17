using System.Net;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public class AppStartupServiceTests
{
    private static Mock<IAuthenticationCoordinator> NotAuthenticatedCoordinator()
    {
        var coordinator = new Mock<IAuthenticationCoordinator>();
        coordinator.SetupGet(item => item.IsAuthenticated).Returns(false);
        coordinator
            .Setup(item => item.TryClaimAuthenticationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return coordinator;
    }

    [Fact]
    public async Task TryRestoreSession_NoRefreshToken_RemainsSignedOut()
    {
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(store => store.GetRefreshTokenAsync()).ReturnsAsync((string?)null);
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = NotAuthenticatedCoordinator();
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
            coordinator.Object,
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        await service.TryRestoreSessionAsync();

        authenticationClient.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryRestoreSession_ValidRefreshToken_StoresRotationAndNavigates()
    {
        var rotatedTokens = new AuthenticationTokensResponse(
            "new-access",
            "new-refresh",
            DateTime.UtcNow.AddMinutes(15));
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(store => store.GetRefreshTokenAsync()).ReturnsAsync("existing-refresh");
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.RefreshAsync(
                "existing-refresh",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rotatedTokens);
        var navigator = new Mock<IAuthenticationNavigator>();
        var coordinator = NotAuthenticatedCoordinator();
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
            coordinator.Object,
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        await service.TryRestoreSessionAsync();

        tokenStore.Verify(store => store.StoreAsync(rotatedTokens), Times.Once);
        navigator.Verify(item => item.ShowAuthenticatedAppAsync(), Times.Once);
        coordinator.Verify(
            item => item.TryClaimAuthenticationAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryRestoreSession_RefreshTokenRejectedByServer_ClearsStoredCredentials()
    {
        // A definitive rejection: the auth/refresh endpoint's explicit 401 refusal (ApiExceptionHandler
        // passes 401 through unchanged, so it surfaces as a plain HttpRequestException with StatusCode
        // populated by EnsureSuccessStatusCode). The refresh token is genuinely dead here.
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(store => store.GetRefreshTokenAsync()).ReturnsAsync("invalid-refresh");
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.RefreshAsync(
                "invalid-refresh",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized));
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = NotAuthenticatedCoordinator();
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
            coordinator.Object,
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        await service.TryRestoreSessionAsync();

        tokenStore.Verify(store => store.ClearAsync(), Times.Once);
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryRestoreSession_RefreshRejectedWithClientError_ClearsStoredCredentials()
    {
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(store => store.GetRefreshTokenAsync()).ReturnsAsync("invalid-refresh");
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.RefreshAsync(
                "invalid-refresh",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException(
                HttpStatusCode.BadRequest,
                "API request failed with status 400.",
                "Bad request",
                "The refresh token is invalid."));
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = NotAuthenticatedCoordinator();
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
            coordinator.Object,
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        await service.TryRestoreSessionAsync();

        tokenStore.Verify(store => store.ClearAsync(), Times.Once);
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryRestoreSession_RefreshFailsWithConnectivityError_KeepsStoredCredentials()
    {
        // A briefly-offline phone must not lose its refresh token: a bare HttpRequestException with no
        // status code (offline, DNS, timeout, or a 5xx from the refresh endpoint) is not evidence the
        // token was rejected.
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(store => store.GetRefreshTokenAsync()).ReturnsAsync("existing-refresh");
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.RefreshAsync(
                "existing-refresh",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("No network connection."));
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = NotAuthenticatedCoordinator();
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
            coordinator.Object,
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        await service.TryRestoreSessionAsync();

        tokenStore.Verify(store => store.ClearAsync(), Times.Never);
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryRestoreSession_StoreAsyncThrowsAfterSuccessfulRefresh_StillShowsAuthenticatedApp()
    {
        // Regression for the "wedge" failure mode: TryClaimAuthenticationAsync has already claimed
        // completion by the time StoreAsync runs, so there is no way back to a re-claimable state. If
        // persisting the refreshed tokens fails, the app must still navigate to the authenticated Shell
        // for this session rather than leaving the coordinator claimed with no UI transition.
        var rotatedTokens = new AuthenticationTokensResponse(
            "new-access",
            "new-refresh",
            DateTime.UtcNow.AddMinutes(15));
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(store => store.GetRefreshTokenAsync()).ReturnsAsync("existing-refresh");
        tokenStore
            .Setup(store => store.StoreAsync(rotatedTokens))
            .ThrowsAsync(new InvalidOperationException("secure storage unavailable"));
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.RefreshAsync(
                "existing-refresh",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rotatedTokens);
        var navigator = new Mock<IAuthenticationNavigator>();
        var coordinator = NotAuthenticatedCoordinator();
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
            coordinator.Object,
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        var act = () => service.TryRestoreSessionAsync();

        await act.Should().NotThrowAsync();
        navigator.Verify(item => item.ShowAuthenticatedAppAsync(), Times.Once);
    }

    [Fact]
    public async Task TryRestoreSession_ApiModeWithSeedRefreshToken_ClearsTokenWithoutAuthenticating()
    {
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(store => store.GetRefreshTokenAsync()).ReturnsAsync("seed-refresh-token");
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = NotAuthenticatedCoordinator();
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
            coordinator.Object,
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        await service.TryRestoreSessionAsync();

        tokenStore.Verify(store => store.ClearAsync(), Times.Once);
        authenticationClient.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryRestoreSession_AlreadyAuthenticated_BailsWithoutTouchingTokenStoreOrNavigator()
    {
        var tokenStore = new Mock<ISecureTokenStore>(MockBehavior.Strict);
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = new Mock<IAuthenticationCoordinator>();
        coordinator.SetupGet(item => item.IsAuthenticated).Returns(true);
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
            coordinator.Object,
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        await service.TryRestoreSessionAsync();

        tokenStore.VerifyNoOtherCalls();
        authenticationClient.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
        coordinator.Verify(
            item => item.TryClaimAuthenticationAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TryRestoreSession_SignInClaimsAuthenticationWhileRefreshInFlight_DoesNotDoubleSwapShell()
    {
        // Simulates the app-link-callback/manual-sign-in-lands-first ordering: the entry guard
        // sees "not authenticated yet" (nothing has landed yet), but by the time the in-flight
        // RefreshAsync call returns, the other completion path has already atomically claimed
        // authentication and shown the Shell. Restore must not swap it a second time.
        var rotatedTokens = new AuthenticationTokensResponse(
            "new-access",
            "new-refresh",
            DateTime.UtcNow.AddMinutes(15));
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(store => store.GetRefreshTokenAsync()).ReturnsAsync("existing-refresh");
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.RefreshAsync(
                "existing-refresh",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rotatedTokens);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = new Mock<IAuthenticationCoordinator>();
        coordinator.SetupGet(item => item.IsAuthenticated).Returns(false);
        coordinator
            .Setup(item => item.TryClaimAuthenticationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
            coordinator.Object,
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        await service.TryRestoreSessionAsync();

        tokenStore.Verify(store => store.StoreAsync(It.IsAny<AuthenticationTokensResponse>()), Times.Never);
        navigator.VerifyNoOtherCalls();
    }
}
