using FluentAssertions;
using FluentValidation;
using Moq;
using SouthBaySoccer.Application.Features.Authentication;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Authentication;

public sealed class VerifyWhatsAppChallengeCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenChallengeAndIdentityAreValid_IssuesTokensForResolvedSubject()
    {
        var identityUserId = Guid.NewGuid();
        var playerProfileId = Guid.NewGuid();
        var roles = new[] { "Player", "Captain" };
        var expiresAtUtc = new DateTime(2026, 6, 26, 13, 0, 0, DateTimeKind.Utc);
        AuthenticationTokenSubject? capturedSubject = null;
        var challengeService = new Mock<IWhatsAppChallengeService>(MockBehavior.Strict);
        challengeService
            .Setup(service => service.VerifyChallengeAsync(
                It.Is<WhatsAppChallengeVerificationRequest>(request =>
                    request.ChallengeToken == "abcdefghijklmnopqrstuvwxyz" &&
                    request.CallbackUri == "southbaysoccer://auth/whatsapp"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppChallengeVerificationResult("phone-hash-7233", "***-***-7233"));
        var identityResolver = new Mock<IWhatsAppIdentityResolver>(MockBehavior.Strict);
        identityResolver
            .Setup(resolver => resolver.FindByVerifiedPhoneNumberHashAsync("phone-hash-7233", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppIdentity(identityUserId, playerProfileId, "***-***-7233", roles));
        var tokenIssuer = new Mock<IAuthenticationTokenIssuer>(MockBehavior.Strict);
        tokenIssuer
            .Setup(issuer => issuer.IssueTokensAsync(It.IsAny<AuthenticationTokenSubject>(), It.IsAny<CancellationToken>()))
            .Callback<AuthenticationTokenSubject, CancellationToken>((subject, _) => capturedSubject = subject)
            .ReturnsAsync(new AuthenticationTokenSet("access-token", "refresh-token", expiresAtUtc));
        var handler = CreateHandler(challengeService, identityResolver, tokenIssuer);

        var result = await handler.HandleAsync(
            new VerifyWhatsAppChallengeCommand(
                "abcdefghijklmnopqrstuvwxyz",
                "southbaysoccer://auth/whatsapp"));

        result.IdentityUserId.Should().Be(identityUserId);
        result.PlayerProfileId.Should().Be(playerProfileId);
        result.MaskedPhoneNumber.Should().Be("***-***-7233");
        result.Roles.Should().BeEquivalentTo(roles);
        result.Tokens.Should().Be(new AuthenticationTokenSet("access-token", "refresh-token", expiresAtUtc));
        capturedSubject.Should().Be(new AuthenticationTokenSubject(identityUserId, playerProfileId, roles));
        challengeService.VerifyAll();
        identityResolver.VerifyAll();
        tokenIssuer.VerifyAll();
    }

    [Fact]
    public async Task HandleAsync_WhenCommandIsInvalid_ThrowsValidationExceptionAndDoesNotVerifyChallenge()
    {
        var challengeService = new Mock<IWhatsAppChallengeService>(MockBehavior.Strict);
        var identityResolver = new Mock<IWhatsAppIdentityResolver>(MockBehavior.Strict);
        var tokenIssuer = new Mock<IAuthenticationTokenIssuer>(MockBehavior.Strict);
        var handler = CreateHandler(challengeService, identityResolver, tokenIssuer);

        var act = async () => await handler.HandleAsync(
            new VerifyWhatsAppChallengeCommand("short", "southbaysoccer://auth/whatsapp"));

        await act.Should().ThrowAsync<ValidationException>();
        challengeService.Verify(
            service => service.VerifyChallengeAsync(
                It.IsAny<WhatsAppChallengeVerificationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        identityResolver.Verify(
            resolver => resolver.FindByVerifiedPhoneNumberHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        tokenIssuer.Verify(
            issuer => issuer.IssueTokensAsync(It.IsAny<AuthenticationTokenSubject>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenVerifiedPhoneHasNoIdentity_ThrowsMaskedExceptionAndDoesNotIssueTokens()
    {
        var challengeService = new Mock<IWhatsAppChallengeService>(MockBehavior.Strict);
        challengeService
            .Setup(service => service.VerifyChallengeAsync(
                It.IsAny<WhatsAppChallengeVerificationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppChallengeVerificationResult("phone-hash-7233", "***-***-7233"));
        var identityResolver = new Mock<IWhatsAppIdentityResolver>(MockBehavior.Strict);
        identityResolver
            .Setup(resolver => resolver.FindByVerifiedPhoneNumberHashAsync("phone-hash-7233", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WhatsAppIdentity?)null);
        var tokenIssuer = new Mock<IAuthenticationTokenIssuer>(MockBehavior.Strict);
        var handler = CreateHandler(challengeService, identityResolver, tokenIssuer);

        var act = async () => await handler.HandleAsync(
            new VerifyWhatsAppChallengeCommand(
                "abcdefghijklmnopqrstuvwxyz",
                "southbaysoccer://auth/whatsapp"));

        var exception = await act.Should().ThrowAsync<WhatsAppIdentityNotFoundException>();
        exception.Which.MaskedPhoneNumber.Should().Be("***-***-7233");
        exception.Which.Message.Should().Contain("***-***-7233");
        exception.Which.Message.Should().NotContain("+15163447233");
        exception.Which.Message.Should().NotContain("abcdefghijklmnopqrstuvwxyz");
        tokenIssuer.Verify(
            issuer => issuer.IssueTokensAsync(It.IsAny<AuthenticationTokenSubject>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static VerifyWhatsAppChallengeCommandHandler CreateHandler(
        Mock<IWhatsAppChallengeService> challengeService,
        Mock<IWhatsAppIdentityResolver> identityResolver,
        Mock<IAuthenticationTokenIssuer> tokenIssuer)
    {
        return new VerifyWhatsAppChallengeCommandHandler(
            new VerifyWhatsAppChallengeCommandValidator(),
            challengeService.Object,
            identityResolver.Object,
            tokenIssuer.Object);
    }
}

