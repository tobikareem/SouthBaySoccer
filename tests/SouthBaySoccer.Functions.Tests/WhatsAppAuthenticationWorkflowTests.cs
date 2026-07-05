using FluentAssertions;
using FluentValidation;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Tests;

public sealed class WhatsAppAuthenticationWorkflowTests
{
    [Fact]
    public async Task RefreshAsync_WhenRefreshTokenRotates_IssuesNewAccessTokenAndReturnsReplacementRefreshToken()
    {
        var identityUserId = Guid.NewGuid();
        var playerProfileId = Guid.NewGuid();
        var accessTokenExpiresAtUtc = new DateTime(2026, 6, 26, 22, 0, 0, DateTimeKind.Utc);
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
                accessTokenExpiresAtUtc.AddDays(30)));
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
            .Returns(new IssuedAccessToken("new-access", accessTokenExpiresAtUtc, "active-key"));
        var workflow = CreateWorkflow(refreshTokenExchange.Object, identityResolver.Object, tokenService.Object);

        var response = await workflow.RefreshAsync(new RefreshTokenRequest("old-refresh"), CancellationToken.None);

        response.AccessToken.Should().Be("new-access");
        response.RefreshToken.Should().Be("new-refresh");
        response.AccessTokenExpiresAtUtc.Should().Be(accessTokenExpiresAtUtc);
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
        var workflow = CreateWorkflow(
            refreshTokenExchange.Object,
            Mock.Of<IWhatsAppIdentityResolver>(),
            Mock.Of<ITokenService>());

        var act = () => workflow.RefreshAsync(new RefreshTokenRequest("bad-refresh"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthenticatedException>();
    }

    private static WhatsAppAuthenticationWorkflow CreateWorkflow(
        IRefreshTokenExchangeService refreshTokenExchangeService,
        IWhatsAppIdentityResolver identityResolver,
        ITokenService tokenService)
    {
        var requestHandler = new RequestWhatsAppChallengeCommandHandler(
            Mock.Of<IValidator<RequestWhatsAppChallengeCommand>>(),
            Mock.Of<IWhatsAppChallengeService>());
        var verifyHandler = new VerifyWhatsAppChallengeCommandHandler(
            Mock.Of<IValidator<VerifyWhatsAppChallengeCommand>>(),
            Mock.Of<IWhatsAppChallengeService>(),
            identityResolver,
            Mock.Of<IAuthenticationTokenIssuer>());

        var signInHandler = new SignInByPhoneCommandHandler(
            new SignInByPhoneCommandValidator(),
            Mock.Of<IPickupPalUserClient>(),
            Mock.Of<IPickupPalUserSyncService>(),
            Mock.Of<IAuthenticationTokenIssuer>());

        return new WhatsAppAuthenticationWorkflow(
            requestHandler,
            verifyHandler,
            signInHandler,
            refreshTokenExchangeService,
            identityResolver,
            tokenService);
    }
}
