using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Authentication;

public sealed class BeginPhoneSignInCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc);

    private static readonly PickupPalUser PickupPalUser = new(
        "cmnddr1ol000ecavpt108stw7",
        "player@pickuppal.test",
        "15106949421",
        "Vic",
        "A",
        null,
        null,
        Array.Empty<string>(),
        Now);

    private readonly Mock<IPickupPalUserClient> pickupPalClient = new();
    private readonly Mock<IPickupPalUserSyncService> syncService = new();
    private readonly Mock<IAuthenticationTokenIssuer> tokenIssuer = new(MockBehavior.Strict);
    private readonly Mock<IPendingPhoneSignInRepository> pendingSignIns = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<IOnboardingPolicy> policy = new();

    public BeginPhoneSignInCommandHandlerTests()
    {
        policy.SetupGet(x => x.RequireWhatsAppVerification).Returns(true);
        policy.SetupGet(x => x.PendingSignInLifetime).Returns(TimeSpan.FromMinutes(15));
        policy.Setup(x => x.IsVerificationExempt(It.IsAny<string>())).Returns(false);
        pickupPalClient
            .Setup(x => x.FindByPhoneAsync("15106949421", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalUser);
    }

    [Fact]
    public async Task HandleAsync_WhenVerificationRequired_RecordsPendingSignInAndIssuesNoTokens()
    {
        PendingPhoneSignIn? recorded = null;
        pendingSignIns
            .Setup(x => x.AddAsync(It.IsAny<PendingPhoneSignIn>(), It.IsAny<CancellationToken>()))
            .Callback<PendingPhoneSignIn, CancellationToken>((pending, _) => recorded = pending)
            .Returns(Task.CompletedTask);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new BeginPhoneSignInCommand("+1 (510) 694-9421"));

        result.VerificationRequired.Should().BeTrue();
        result.Tokens.Should().BeNull();
        result.PhoneMasked.Should().Be("+******9421");
        result.DisplayName.Should().Be("Vic");
        recorded.Should().NotBeNull();
        recorded!.PickupPalUserId.Should().Be(PickupPalUser.Id);
        recorded.PhoneNumberHash.Should().NotContain("9421").And.HaveLength(64);
        recorded.ExpiresAtUtc.Should().Be(Now.AddMinutes(15));
        recorded.ConsumedAtUtc.Should().BeNull();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        syncService.Verify(x => x.SyncAsync(It.IsAny<PickupPalUser>(), It.IsAny<CancellationToken>()), Times.Never);
        tokenIssuer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenPhoneIsVerificationExempt_SyncsAndIssuesTokensWithoutPendingSignIn()
    {
        policy.Setup(x => x.IsVerificationExempt("+15106949421")).Returns(true);
        var (subject, tokens) = SetupSyncAndIssue();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new BeginPhoneSignInCommand("5106949421"));

        result.VerificationRequired.Should().BeFalse();
        result.Tokens.Should().Be(tokens);
        tokenIssuer.Verify(x => x.IssueTokensAsync(subject, It.IsAny<CancellationToken>()), Times.Once);
        pendingSignIns.Verify(x => x.AddAsync(It.IsAny<PendingPhoneSignIn>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenVerificationDisabled_IssuesTokensLikeLegacyPhoneSignIn()
    {
        policy.SetupGet(x => x.RequireWhatsAppVerification).Returns(false);
        var (_, tokens) = SetupSyncAndIssue();
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new BeginPhoneSignInCommand("+1 (510) 694-9421"));

        result.VerificationRequired.Should().BeFalse();
        result.Tokens.Should().Be(tokens);
        pendingSignIns.Verify(x => x.AddAsync(It.IsAny<PendingPhoneSignIn>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenTenDigitNumberLacksCountryCode_LooksUpWithLeadingOne()
    {
        // "5106949421" (no country code) must hit Pickup Pal as "15106949421", since Pickup Pal
        // keys US users by their 11-digit number.
        var strictClient = new Mock<IPickupPalUserClient>(MockBehavior.Strict);
        strictClient
            .Setup(x => x.FindByPhoneAsync("15106949421", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalUser);
        var handler = CreateHandler(strictClient.Object);

        var result = await handler.HandleAsync(new BeginPhoneSignInCommand("(510) 694-9421"));

        result.VerificationRequired.Should().BeTrue();
        strictClient.Verify(x => x.FindByPhoneAsync("15106949421", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("5106949421", "15106949421")]
    [InlineData("15106949421", "15106949421")]
    [InlineData("+15106949421", "15106949421")]
    [InlineData("442079460958", "442079460958")]
    // 8- and 9-digit numbers sit inside the validator's accepted range but are not US-shaped;
    // they must pass through untouched.
    [InlineData("12345678", "12345678")]
    [InlineData("123456789", "123456789")]
    public void NormalizeDigits_TenDigitNumbersGainUsCountryCode_OthersUnchanged(string input, string expected)
    {
        BeginPhoneSignInCommandValidator.NormalizeDigits(input).Should().Be(expected);
    }

    [Fact]
    public async Task HandleAsync_WhenPickupPalUserMissing_ThrowsAndRecordsNothing()
    {
        pickupPalClient
            .Setup(x => x.FindByPhoneAsync("15106949421", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PickupPalUser?)null);
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(new BeginPhoneSignInCommand("+1 (510) 694-9421"));

        await act.Should().ThrowAsync<PickupPalUserNotFoundException>();
        pendingSignIns.Verify(x => x.AddAsync(It.IsAny<PendingPhoneSignIn>(), It.IsAny<CancellationToken>()), Times.Never);
        tokenIssuer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenPhoneNumberInvalid_ThrowsValidationException()
    {
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(new BeginPhoneSignInCommand("12"));

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    private (AuthenticationTokenSubject Subject, AuthenticationTokenSet Tokens) SetupSyncAndIssue()
    {
        var subject = new AuthenticationTokenSubject(Guid.NewGuid(), Guid.NewGuid(), new[] { "Player" });
        var tokens = new AuthenticationTokenSet("access", "refresh", Now.AddMinutes(15));
        syncService.Setup(x => x.SyncAsync(PickupPalUser, It.IsAny<CancellationToken>())).ReturnsAsync(subject);
        tokenIssuer.Setup(x => x.IssueTokensAsync(subject, It.IsAny<CancellationToken>())).ReturnsAsync(tokens);
        return (subject, tokens);
    }

    private BeginPhoneSignInCommandHandler CreateHandler(IPickupPalUserClient? userClient = null)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);

        return new BeginPhoneSignInCommandHandler(
            new BeginPhoneSignInCommandValidator(),
            userClient ?? pickupPalClient.Object,
            syncService.Object,
            tokenIssuer.Object,
            pendingSignIns.Object,
            unitOfWork.Object,
            clock.Object,
            policy.Object);
    }
}
