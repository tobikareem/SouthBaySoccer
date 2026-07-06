using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Client.Tests;

public class AppStartupServiceTests
{
    [Fact]
    public async Task TryRestoreSession_NoRefreshToken_RemainsSignedOut()
    {
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(store => store.GetRefreshTokenAsync()).ReturnsAsync((string?)null);
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
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
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        await service.TryRestoreSessionAsync();

        tokenStore.Verify(store => store.StoreAsync(rotatedTokens), Times.Once);
        navigator.Verify(item => item.ShowAuthenticatedAppAsync(), Times.Once);
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
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
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
        var service = new AppStartupService(
            tokenStore.Object,
            authenticationClient.Object,
            navigator.Object,
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        await service.TryRestoreSessionAsync();

        tokenStore.Verify(store => store.ClearAsync(), Times.Once);
        authenticationClient.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }
}

