using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Tests;

public sealed class AuthenticationWorkflowTests
{
    private static readonly DateTime Now = new(2026, 6, 26, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RefreshAsync_WhenRefreshTokenRotates_IssuesNewAccessTokenAndReturnsReplacementRefreshToken()
    {
        var identityUserId = Guid.NewGuid();
        var playerProfileId = Guid.NewGuid();
        AccessTokenIssueRequest? issuedRequest = null;
        var refreshTokenExchange = new Mock<IRefreshTokenExchangeService>();
        refreshTokenExchange
            .Setup(x => x.RotateAsync(
                It.Is<RefreshTokenExchangeRequest>(request => request.RefreshToken == "old-refresh"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenExchangeResult(
                RefreshTokenExchangeStatus.Rotated,
                identityUserId,
                playerProfileId,
                "new-refresh",
                Guid.NewGuid(),
                Now.AddDays(30)));
        var identityResolver = new Mock<IWhatsAppIdentityResolver>();
        identityResolver
            .Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppIdentity(
                identityUserId,
                playerProfileId,
                "+1******0123",
                new[] { PlayerRole.Captain.ToString() }));
        var tokenService = new Mock<ITokenService>();
        tokenService
            .Setup(x => x.IssueAccessToken(It.IsAny<AccessTokenIssueRequest>()))
            .Callback<AccessTokenIssueRequest>(request => issuedRequest = request)
            .Returns(new IssuedAccessToken("new-access", Now, "active-key"));
        var workflow = CreateWorkflow(
            refreshTokenExchangeService: refreshTokenExchange.Object,
            identityResolver: identityResolver.Object,
            tokenService: tokenService.Object);

        var response = await workflow.RefreshAsync(new RefreshTokenRequest("old-refresh"), CancellationToken.None);

        response.AccessToken.Should().Be("new-access");
        response.RefreshToken.Should().Be("new-refresh");
        response.AccessTokenExpiresAtUtc.Should().Be(Now);
        issuedRequest.Should().NotBeNull();
        issuedRequest!.UserId.Should().Be(identityUserId);
        issuedRequest.Roles.Should().ContainSingle().Which.Should().Be(PlayerRole.Captain.ToString());
        issuedRequest.Policies.Should().Contain("AuthenticatedPlayer");
        issuedRequest.Policies.Should().Contain("CanAssignTeams");
        issuedRequest.Policies.Should().Contain("CanRecordStats");
    }

    [Fact]
    public async Task RefreshAsync_WhenRefreshTokenIsRejected_ThrowsUnauthenticatedException()
    {
        var refreshTokenExchange = new Mock<IRefreshTokenExchangeService>();
        refreshTokenExchange
            .Setup(x => x.RotateAsync(It.IsAny<RefreshTokenExchangeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenExchangeResult(RefreshTokenExchangeStatus.Invalid));
        var workflow = CreateWorkflow(refreshTokenExchangeService: refreshTokenExchange.Object);

        var act = () => workflow.RefreshAsync(new RefreshTokenRequest("bad-refresh"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthenticatedException>();
    }

    [Fact]
    public async Task BeginPhoneSignInAsync_WhenVerificationRequired_ReturnsMaskedPhoneAndNoTokens()
    {
        var userClient = new Mock<IPickupPalUserClient>();
        userClient
            .Setup(x => x.FindByPhoneAsync("15106949421", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalUser("pp-1", null, "15106949421", "Vic", "A", null, null, Array.Empty<string>(), null));
        var workflow = CreateWorkflow(userClient: userClient.Object);

        var response = await workflow.BeginPhoneSignInAsync(new SignInByPhoneRequest("(510) 694-9421"), CancellationToken.None);

        response.VerificationRequired.Should().BeTrue();
        response.PhoneMasked.Should().Be("+******9421");
        response.DisplayName.Should().Be("Vic");
        response.Tokens.Should().BeNull();
    }

    [Fact]
    public async Task SignOutAsync_WhenRefreshTokenPresented_RevokesItsFamily()
    {
        var userId = Guid.NewGuid();
        var revocation = new Mock<IRefreshTokenRevocationService>();
        var workflow = CreateWorkflow(refreshTokenRevocationService: revocation.Object);

        await workflow.SignOutAsync(new SignOutCommand(userId, "refresh-secret"), CancellationToken.None);

        revocation.Verify(
            x => x.RevokeFamilyAsync(userId, "refresh-secret", "SignOut", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SignOutAsync_WhenNoRefreshTokenPresented_RevokesNothing()
    {
        var revocation = new Mock<IRefreshTokenRevocationService>(MockBehavior.Strict);
        var workflow = CreateWorkflow(refreshTokenRevocationService: revocation.Object);

        await workflow.SignOutAsync(new SignOutCommand(Guid.NewGuid(), null), CancellationToken.None);

        revocation.VerifyNoOtherCalls();
    }

    private static AuthenticationWorkflow CreateWorkflow(
        IRefreshTokenExchangeService? refreshTokenExchangeService = null,
        IRefreshTokenRevocationService? refreshTokenRevocationService = null,
        IWhatsAppIdentityResolver? identityResolver = null,
        ITokenService? tokenService = null,
        IPickupPalUserClient? userClient = null)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);
        var policy = new Mock<IOnboardingPolicy>();
        policy.SetupGet(x => x.RequireWhatsAppVerification).Returns(true);
        policy.SetupGet(x => x.PendingSignInLifetime).Returns(TimeSpan.FromMinutes(15));
        policy.SetupGet(x => x.RememberDeviceRefreshTokenLifetime).Returns(TimeSpan.FromDays(30));

        var beginHandler = new BeginPhoneSignInCommandHandler(
            new BeginPhoneSignInCommandValidator(),
            userClient ?? Mock.Of<IPickupPalUserClient>(),
            Mock.Of<IPickupPalUserSyncService>(),
            Mock.Of<IAuthenticationTokenIssuer>(),
            Mock.Of<IPendingPhoneSignInRepository>(),
            Mock.Of<IUnitOfWork>(),
            clock.Object,
            policy.Object);
        var completeHandler = new CompleteWhatsAppLoginCommandHandler(
            new CompleteWhatsAppLoginCommandValidator(),
            Mock.Of<IPickupPalOnboardingClient>(),
            Mock.Of<IPendingPhoneSignInRepository>(),
            Mock.Of<IUnitOfWork>(),
            clock.Object,
            Mock.Of<IPickupPalUserSyncService>(),
            Mock.Of<IAuthenticationTokenIssuer>(),
            policy.Object);

        return new AuthenticationWorkflow(
            beginHandler,
            completeHandler,
            refreshTokenExchangeService ?? Mock.Of<IRefreshTokenExchangeService>(),
            refreshTokenRevocationService ?? Mock.Of<IRefreshTokenRevocationService>(),
            identityResolver ?? Mock.Of<IWhatsAppIdentityResolver>(),
            tokenService ?? Mock.Of<ITokenService>());
    }
}
