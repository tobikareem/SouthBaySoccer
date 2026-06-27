using FluentAssertions;
using FluentValidation;
using Moq;
using SouthBaySoccer.Application.Features.Authentication;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Authentication;

public sealed class RequestWhatsAppChallengeCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenCommandIsValid_CreatesChallengeWithMaskedPhone()
    {
        var expiresAtUtc = new DateTime(2026, 6, 26, 12, 0, 0, DateTimeKind.Utc);
        WhatsAppChallengeIssueRequest? capturedRequest = null;
        var challengeService = new Mock<IWhatsAppChallengeService>(MockBehavior.Strict);
        challengeService
            .Setup(service => service.CreateChallengeAsync(
                It.IsAny<WhatsAppChallengeIssueRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<WhatsAppChallengeIssueRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new WhatsAppChallengeIssueResult("challenge-123", "+15163447233", expiresAtUtc));
        var handler = new RequestWhatsAppChallengeCommandHandler(
            new RequestWhatsAppChallengeCommandValidator(),
            challengeService.Object);

        var result = await handler.HandleAsync(
            new RequestWhatsAppChallengeCommand("+15163447233", "southbaysoccer://auth/whatsapp"));

        result.Should().Be(new RequestWhatsAppChallengeResult("challenge-123", "***-***-7233", expiresAtUtc));
        result.MaskedPhoneNumber.Should().NotContain("15163447233");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.PhoneNumber.Should().Be("+15163447233");
        capturedRequest.MaskedPhoneNumber.Should().Be("***-***-7233");
        capturedRequest.CallbackUri.Should().Be("southbaysoccer://auth/whatsapp");
        challengeService.VerifyAll();
    }

    [Fact]
    public async Task HandleAsync_WhenPhoneIsInvalid_ThrowsValidationExceptionAndDoesNotCreateChallenge()
    {
        var challengeService = new Mock<IWhatsAppChallengeService>(MockBehavior.Strict);
        var handler = new RequestWhatsAppChallengeCommandHandler(
            new RequestWhatsAppChallengeCommandValidator(),
            challengeService.Object);

        var act = async () => await handler.HandleAsync(
            new RequestWhatsAppChallengeCommand("516-344-7233", "southbaysoccer://auth/whatsapp"));

        await act.Should().ThrowAsync<ValidationException>();
        challengeService.Verify(
            service => service.CreateChallengeAsync(
                It.IsAny<WhatsAppChallengeIssueRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Mask_WhenPhoneContainsFormatting_ReturnsOnlySafeSuffix()
    {
        var result = PhoneNumberMasker.Mask("+1 (516) 344-7233");

        result.Should().Be("***-***-7233");
        result.Should().NotContain("516");
        result.Should().NotContain("344");
    }
}


