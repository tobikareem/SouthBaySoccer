using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Authentication;

public sealed class CompleteWhatsAppLoginCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan RememberLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

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

    private readonly Mock<IPickupPalOnboardingClient> onboardingClient = new();
    private readonly Mock<IPendingPhoneSignInRepository> pendingSignIns = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<IPickupPalUserSyncService> syncService = new();
    private readonly Mock<IAuthenticationTokenIssuer> tokenIssuer = new(MockBehavior.Strict);
    private readonly AuthenticationTokenSubject subject = new(Guid.NewGuid(), Guid.NewGuid(), new[] { "Player" });
    private readonly AuthenticationTokenSet tokens = new("access", "refresh", Now.AddMinutes(15));

    public CompleteWhatsAppLoginCommandHandlerTests()
    {
        onboardingClient
            .Setup(x => x.RedeemLoginTokenAsync("login-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalUser);
        syncService.Setup(x => x.SyncAsync(PickupPalUser, It.IsAny<CancellationToken>())).ReturnsAsync(subject);
    }

    [Fact]
    public async Task HandleAsync_WhenPendingSignInMatches_ConsumesItSyncsAndIssuesTokens()
    {
        var pending = ActivePendingSignIn();
        pendingSignIns
            .Setup(x => x.ListActiveByPickupPalUserIdAsync(PickupPalUser.Id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([pending]);
        tokenIssuer.Setup(x => x.IssueTokensAsync(subject, SessionLifetime, It.IsAny<CancellationToken>())).ReturnsAsync(tokens);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CompleteWhatsAppLoginCommand("login-token", RememberDevice: false));

        result.Should().Be(tokens);
        pending.ConsumedAtUtc.Should().Be(Now);
        pending.RememberDevice.Should().BeFalse();
        pendingSignIns.Verify(x => x.Update(pending), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // A session sign-in gets the short lifetime, never the default or remembered one.
        tokenIssuer.Verify(x => x.IssueTokensAsync(subject, SessionLifetime, It.IsAny<CancellationToken>()), Times.Once);
        SessionLifetime.Should().BeLessThan(RememberLifetime);
    }

    [Fact]
    public async Task HandleAsync_WhenSeveralPendingSignInsAreActive_ConsumesEveryOne()
    {
        var newest = ActivePendingSignIn();
        var older = ActivePendingSignIn();
        older.CreatedAt = Now.AddMinutes(-10);
        pendingSignIns
            .Setup(x => x.ListActiveByPickupPalUserIdAsync(PickupPalUser.Id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([newest, older]);
        tokenIssuer.Setup(x => x.IssueTokensAsync(subject, SessionLifetime, It.IsAny<CancellationToken>())).ReturnsAsync(tokens);
        var handler = CreateHandler();

        await handler.HandleAsync(new CompleteWhatsAppLoginCommand("login-token", false));

        newest.ConsumedAtUtc.Should().Be(Now);
        older.ConsumedAtUtc.Should().Be(Now);
        pendingSignIns.Verify(x => x.Update(It.IsAny<PendingPhoneSignIn>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_WhenConcurrentCompletionWinsTheRowVersion_ThrowsMismatchAndIssuesNoTokens()
    {
        // Two devices open the same login link at once: the second SaveChanges hits the row-version
        // conflict (surfaced by the unit of work as a conflict) and must not get tokens.
        var pending = ActivePendingSignIn();
        pendingSignIns
            .Setup(x => x.ListActiveByPickupPalUserIdAsync(PickupPalUser.Id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([pending]);
        unitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationConflictException("The resource changed while this request was being saved."));
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(new CompleteWhatsAppLoginCommand("login-token", false));

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(OnboardingTokenFailure.Mismatch);
        syncService.Verify(x => x.SyncAsync(It.IsAny<PickupPalUser>(), It.IsAny<CancellationToken>()), Times.Never);
        tokenIssuer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenRememberDevice_IssuesTokensWithRememberDeviceLifetime()
    {
        var pending = ActivePendingSignIn();
        pendingSignIns
            .Setup(x => x.ListActiveByPickupPalUserIdAsync(PickupPalUser.Id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([pending]);
        tokenIssuer
            .Setup(x => x.IssueTokensAsync(subject, RememberLifetime, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
        var handler = CreateHandler();

        var result = await handler.HandleAsync(new CompleteWhatsAppLoginCommand("login-token", RememberDevice: true));

        result.Should().Be(tokens);
        pending.RememberDevice.Should().BeTrue();
        tokenIssuer.Verify(x => x.IssueTokensAsync(subject, RememberLifetime, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenTokenResolvesToUserWithoutPendingSignIn_ThrowsMismatchAndIssuesNoTokens()
    {
        pendingSignIns
            .Setup(x => x.ListActiveByPickupPalUserIdAsync(PickupPalUser.Id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(new CompleteWhatsAppLoginCommand("login-token", false));

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(OnboardingTokenFailure.Mismatch);
        syncService.Verify(x => x.SyncAsync(It.IsAny<PickupPalUser>(), It.IsAny<CancellationToken>()), Times.Never);
        tokenIssuer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenPendingSignInExpired_ThrowsMismatch()
    {
        var expired = ActivePendingSignIn();
        expired.ExpiresAtUtc = Now.AddSeconds(-1);
        pendingSignIns
            .Setup(x => x.ListActiveByPickupPalUserIdAsync(PickupPalUser.Id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expired]);
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(new CompleteWhatsAppLoginCommand("login-token", false));

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(OnboardingTokenFailure.Mismatch);
        tokenIssuer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenPendingSignInAlreadyConsumed_ThrowsMismatch()
    {
        var consumed = ActivePendingSignIn();
        consumed.ConsumedAtUtc = Now.AddMinutes(-1);
        pendingSignIns
            .Setup(x => x.ListActiveByPickupPalUserIdAsync(PickupPalUser.Id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([consumed]);
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(new CompleteWhatsAppLoginCommand("login-token", false));

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(OnboardingTokenFailure.Mismatch);
        tokenIssuer.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PickupPalOnboardingFailure.TokenExpired, OnboardingTokenFailure.Expired)]
    [InlineData(PickupPalOnboardingFailure.TokenInvalid, OnboardingTokenFailure.Invalid)]
    [InlineData(PickupPalOnboardingFailure.Validation, OnboardingTokenFailure.Invalid)]
    public async Task HandleAsync_WhenPickupPalRejectsToken_MapsToTokenFailure(
        PickupPalOnboardingFailure upstream,
        OnboardingTokenFailure expected)
    {
        onboardingClient
            .Setup(x => x.RedeemLoginTokenAsync("login-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PickupPalOnboardingException(upstream));
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(new CompleteWhatsAppLoginCommand("login-token", false));

        (await act.Should().ThrowAsync<OnboardingTokenException>()).Which.Failure.Should().Be(expected);
        pendingSignIns.Verify(
            x => x.ListActiveByPickupPalUserIdAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenTokenBlank_ThrowsValidationException()
    {
        var handler = CreateHandler();

        var act = () => handler.HandleAsync(new CompleteWhatsAppLoginCommand("  ", false));

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    private static PendingPhoneSignIn ActivePendingSignIn() => new()
    {
        Id = Guid.NewGuid(),
        PickupPalUserId = PickupPalUser.Id,
        PhoneNumberHash = new string('A', 64),
        ExpiresAtUtc = Now.AddMinutes(10),
        CreatedAt = Now.AddMinutes(-5),
    };

    private CompleteWhatsAppLoginCommandHandler CreateHandler()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);
        var policy = new Mock<IOnboardingPolicy>();
        policy.SetupGet(x => x.RememberDeviceRefreshTokenLifetime).Returns(RememberLifetime);
        policy.SetupGet(x => x.SessionRefreshTokenLifetime).Returns(SessionLifetime);

        return new CompleteWhatsAppLoginCommandHandler(
            new CompleteWhatsAppLoginCommandValidator(),
            onboardingClient.Object,
            pendingSignIns.Object,
            unitOfWork.Object,
            clock.Object,
            syncService.Object,
            tokenIssuer.Object,
            policy.Object);
    }
}
