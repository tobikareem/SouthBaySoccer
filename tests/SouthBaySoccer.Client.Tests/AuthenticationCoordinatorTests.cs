using FluentAssertions;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

public class AuthenticationCoordinatorTests
{
    [Fact]
    public void IsAuthenticated_BeforeAnySignIn_IsFalse()
    {
        var coordinator = new AuthenticationCoordinator(
            new Mock<IAuthenticationClient>(MockBehavior.Strict).Object,
            new Mock<ISecureTokenStore>(MockBehavior.Strict).Object,
            new Mock<IAuthenticationNavigator>(MockBehavior.Strict).Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        coordinator.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task IsAuthenticated_AfterCompleteSignIn_IsTrue()
    {
        var tokens = new AuthenticationTokensResponse(
            "access-token",
            "refresh-token",
            DateTime.UtcNow.AddMinutes(15));
        var tokenStore = new Mock<ISecureTokenStore>();
        var navigator = new Mock<IAuthenticationNavigator>();
        var coordinator = new AuthenticationCoordinator(
            new Mock<IAuthenticationClient>(MockBehavior.Strict).Object,
            tokenStore.Object,
            navigator.Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        await coordinator.CompleteSignInAsync(tokens);

        coordinator.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task SignOutAsync_ClearsTokensResetsStateAndShowsSignIn()
    {
        var tokens = new AuthenticationTokensResponse(
            "access-token",
            "refresh-token",
            DateTime.UtcNow.AddMinutes(15));
        var tokenStore = new Mock<ISecureTokenStore>();
        var navigator = new Mock<IAuthenticationNavigator>();
        var coordinator = new AuthenticationCoordinator(
            new Mock<IAuthenticationClient>(MockBehavior.Strict).Object,
            tokenStore.Object,
            navigator.Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });
        await coordinator.CompleteSignInAsync(tokens);
        coordinator.IsAuthenticated.Should().BeTrue();

        await coordinator.SignOutAsync();

        coordinator.IsAuthenticated.Should().BeFalse();
        tokenStore.Verify(store => store.ClearAsync(), Times.Once);
        navigator.Verify(nav => nav.ShowSignInAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IsAuthenticated_AfterVerifiedAppLinkCallback_IsTrue()
    {
        var tokens = new AuthenticationTokensResponse(
            "access-token",
            "refresh-token",
            DateTime.UtcNow.AddMinutes(15));
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.VerifyWhatsAppChallengeAsync(
                "verified-token",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
        var tokenStore = new Mock<ISecureTokenStore>();
        var navigator = new Mock<IAuthenticationNavigator>();
        var coordinator = new AuthenticationCoordinator(
            authenticationClient.Object,
            tokenStore.Object,
            navigator.Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        coordinator.IsAuthenticated.Should().BeFalse();

        await coordinator.HandleCallbackAsync(
            new Uri("southbaysoccer://auth/whatsapp?token=verified-token"));

        coordinator.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task TryClaimAuthentication_WhenNotYetAuthenticated_ClaimsAndReturnsTrue()
    {
        var coordinator = new AuthenticationCoordinator(
            new Mock<IAuthenticationClient>(MockBehavior.Strict).Object,
            new Mock<ISecureTokenStore>(MockBehavior.Strict).Object,
            new Mock<IAuthenticationNavigator>(MockBehavior.Strict).Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        var claimed = await coordinator.TryClaimAuthenticationAsync();

        claimed.Should().BeTrue();
        coordinator.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task TryClaimAuthentication_CalledTwice_OnlyTheFirstCallerWins()
    {
        var coordinator = new AuthenticationCoordinator(
            new Mock<IAuthenticationClient>(MockBehavior.Strict).Object,
            new Mock<ISecureTokenStore>(MockBehavior.Strict).Object,
            new Mock<IAuthenticationNavigator>(MockBehavior.Strict).Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        var firstClaim = await coordinator.TryClaimAuthenticationAsync();
        var secondClaim = await coordinator.TryClaimAuthenticationAsync();

        firstClaim.Should().BeTrue();
        secondClaim.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteSignInAsync_WhenRestoreAlreadyClaimedAuthentication_DoesNotStoreOrNavigateAgain()
    {
        // Covers the restore-wins-the-race ordering: session restore atomically claims
        // authentication and shows the Shell first; a manual sign-in that was already in flight
        // must become a no-op instead of storing tokens and swapping the Shell a second time.
        var tokens = new AuthenticationTokensResponse(
            "access-token",
            "refresh-token",
            DateTime.UtcNow.AddMinutes(15));
        var tokenStore = new Mock<ISecureTokenStore>(MockBehavior.Strict);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = new AuthenticationCoordinator(
            new Mock<IAuthenticationClient>(MockBehavior.Strict).Object,
            tokenStore.Object,
            navigator.Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });
        (await coordinator.TryClaimAuthenticationAsync()).Should().BeTrue();

        await coordinator.CompleteSignInAsync(tokens);

        tokenStore.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleCallback_WhenRestoreAlreadyClaimedAuthentication_SkipsVerificationAndReturnsTrue()
    {
        // Same ordering from the callback side: a legitimate but late-arriving WhatsApp callback
        // must not re-verify or re-navigate once restore has already claimed authentication.
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var tokenStore = new Mock<ISecureTokenStore>(MockBehavior.Strict);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = new AuthenticationCoordinator(
            authenticationClient.Object,
            tokenStore.Object,
            navigator.Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });
        (await coordinator.TryClaimAuthenticationAsync()).Should().BeTrue();

        var result = await coordinator.HandleCallbackAsync(
            new Uri("southbaysoccer://auth/whatsapp?token=verified-token"));

        result.Should().BeTrue();
        authenticationClient.VerifyNoOtherCalls();
        tokenStore.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleCallback_ValidConfiguredCallback_StoresTokensAndNavigatesOnce()
    {
        var tokens = new AuthenticationTokensResponse(
            "access-token",
            "refresh-token",
            DateTime.UtcNow.AddMinutes(15));
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.VerifyWhatsAppChallengeAsync(
                "verified-token",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
        var tokenStore = new Mock<ISecureTokenStore>();
        var navigator = new Mock<IAuthenticationNavigator>();
        var coordinator = new AuthenticationCoordinator(
            authenticationClient.Object,
            tokenStore.Object,
            navigator.Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });
        var callback = new Uri("southbaysoccer://auth/whatsapp?token=verified-token");

        var firstResult = await coordinator.HandleCallbackAsync(callback);
        var secondResult = await coordinator.HandleCallbackAsync(callback);

        firstResult.Should().BeTrue();
        secondResult.Should().BeTrue();
        tokenStore.Verify(store => store.StoreAsync(tokens), Times.Once);
        navigator.Verify(item => item.ShowAuthenticatedAppAsync(), Times.Once);
    }

    [Fact]
    public async Task HandleCallback_UnapprovedCallback_DoesNotExchangeOrNavigate()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var tokenStore = new Mock<ISecureTokenStore>(MockBehavior.Strict);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = new AuthenticationCoordinator(
            authenticationClient.Object,
            tokenStore.Object,
            navigator.Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        var result = await coordinator.HandleCallbackAsync(
            new Uri("https://example.com/auth/whatsapp?token=forged"));

        result.Should().BeFalse();
        authenticationClient.VerifyNoOtherCalls();
        tokenStore.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleCallback_ConfiguredSchemeWithoutVerifiedToken_DoesNotAuthenticate()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var tokenStore = new Mock<ISecureTokenStore>(MockBehavior.Strict);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = new AuthenticationCoordinator(
            authenticationClient.Object,
            tokenStore.Object,
            navigator.Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        // Merely returning to the app on the approved scheme (no verified one-time token)
        // must never establish a session (AUTH-8/AUTH-9, INV-11 fail-closed).
        var result = await coordinator.HandleCallbackAsync(
            new Uri("southbaysoccer://auth/whatsapp"));

        result.Should().BeFalse();
        authenticationClient.VerifyNoOtherCalls();
        tokenStore.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryCompleteChallenge_SeedProvider_StoresTokensAndNavigates()
    {
        var tokens = new AuthenticationTokensResponse(
            "seed-access-token",
            "seed-refresh-token",
            DateTime.UtcNow.AddMinutes(15));
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.VerifyWhatsAppChallengeAsync(
                "seed-whatsapp-challenge",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
        var tokenStore = new Mock<ISecureTokenStore>();
        var navigator = new Mock<IAuthenticationNavigator>();
        var coordinator = new AuthenticationCoordinator(
            authenticationClient.Object,
            tokenStore.Object,
            navigator.Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Seed });

        var result = await coordinator.TryCompleteChallengeAsync("seed-whatsapp-challenge");

        result.Should().BeTrue();
        tokenStore.Verify(store => store.StoreAsync(tokens), Times.Once);
        navigator.Verify(item => item.ShowAuthenticatedAppAsync(), Times.Once);
    }

    [Fact]
    public async Task TryCompleteChallenge_ApiProvider_DoesNotBypassVerifiedCallback()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var tokenStore = new Mock<ISecureTokenStore>(MockBehavior.Strict);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = new AuthenticationCoordinator(
            authenticationClient.Object,
            tokenStore.Object,
            navigator.Object,
            new ClientResponseCache(TimeProvider.System),
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        var result = await coordinator.TryCompleteChallengeAsync("unverified-challenge-id");

        result.Should().BeFalse();
        authenticationClient.VerifyNoOtherCalls();
        tokenStore.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignOutAsync_DropsCachedResponsesSoTheNextAccountStartsClean()
    {
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(x => x.ClearAsync()).Returns(Task.CompletedTask);
        var navigator = new Mock<IAuthenticationNavigator>();
        navigator.Setup(x => x.ShowSignInAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var cache = new ClientResponseCache(TimeProvider.System);
        await cache.GetOrCreateAsync("profile:me", TimeSpan.FromMinutes(5), _ => Task.FromResult("previous account"));
        var coordinator = new AuthenticationCoordinator(
            new Mock<IAuthenticationClient>().Object,
            tokenStore.Object,
            navigator.Object,
            cache,
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        await coordinator.SignOutAsync();

        var calls = 0;
        var afterSignOut = await cache.GetOrCreateAsync(
            "profile:me",
            TimeSpan.FromMinutes(5),
            _ => { calls++; return Task.FromResult("next account"); });
        calls.Should().Be(1, "a cached response must never outlive the session that fetched it");
        afterSignOut.Should().Be("next account");
    }
}
