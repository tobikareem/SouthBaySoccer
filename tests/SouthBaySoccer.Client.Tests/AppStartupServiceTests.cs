using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;

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
    public async Task TryRestoreSession_InvalidRefreshToken_ClearsStoredCredentials()
    {
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(store => store.GetRefreshTokenAsync()).ReturnsAsync("invalid-refresh");
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.RefreshAsync(
                "invalid-refresh",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unauthorized"));
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
