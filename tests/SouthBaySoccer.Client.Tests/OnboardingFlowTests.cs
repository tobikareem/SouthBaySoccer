using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Client.Tests;

public class OnboardingFlowTests
{
    private static readonly AuthenticationTokensResponse Tokens =
        new("access", "refresh", new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task StartSignUpHandoff_OpensWhatsAppWithRegisterMessageAndShowsWaiting()
    {
        var harness = new Harness();
        harness.Launcher
            .Setup(l => l.OpenWhatsAppMessageAsync("!!register source=n9jabay", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        harness.Navigator
            .Setup(n => n.ShowLinkWaitingAsync(OnboardingLinkKind.Register, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await harness.Flow.StartSignUpHandoffAsync(CancellationToken.None);

        harness.Flow.PendingKind.Should().Be(OnboardingLinkKind.Register);
        harness.Flow.LinkRequestedAt.Should().Be(harness.Time.GetUtcNow());
        harness.Flow.LastHandoffFailed.Should().BeFalse();
        harness.Navigator.VerifyAll();
    }

    [Fact]
    public async Task StartSignUpHandoff_WhatsAppUnavailable_StillShowsWaitingAndFlagsFailure()
    {
        var harness = new Harness();
        harness.Launcher
            .Setup(l => l.OpenWhatsAppMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        harness.Navigator
            .Setup(n => n.ShowLinkWaitingAsync(OnboardingLinkKind.Register, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await harness.Flow.StartSignUpHandoffAsync(CancellationToken.None);

        harness.Flow.LastHandoffFailed.Should().BeTrue();
        harness.Navigator.VerifyAll();
    }

    [Theory]
    [InlineData("southbaysoccer://auth/register?token=abc")]
    [InlineData("https://n9jabay.app/register?token=abc")]
    [InlineData("https://N9JABAY.app/register/?token=abc&x=1")]
    public async Task HandleAppLink_RegisterLink_ValidatesTokenAndShowsDetails(string link)
    {
        var harness = new Harness();
        harness.Client
            .Setup(c => c.ValidateRegistrationAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationTokenValidationResponse("+1 (555) ••• 9421"));
        harness.Navigator
            .Setup(n => n.ShowSignUpDetailsAsync("abc", "+1 (555) ••• 9421", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handled = await harness.Flow.HandleAppLinkAsync(new Uri(link), CancellationToken.None);

        handled.Should().BeTrue();
        harness.Navigator.VerifyAll();
    }

    [Theory]
    [InlineData("https://example.com/register?token=abc")]
    [InlineData("southbaysoccer://auth/whatsapp?token=abc")]
    [InlineData("southbaysoccer://auth/register")]
    [InlineData("southbaysoccer://auth/register?token=")]
    public async Task HandleAppLink_ForeignOrTokenlessLink_IsNotHandled(string link)
    {
        var harness = new Harness();

        var handled = await harness.Flow.HandleAppLinkAsync(new Uri(link), CancellationToken.None);

        handled.Should().BeFalse();
        harness.Client.VerifyNoOtherCalls();
        harness.Navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAppLink_ExpiredRegisterToken_ShowsExpiredScreen()
    {
        var harness = new Harness();
        harness.Client
            .Setup(c => c.ValidateRegistrationAsync("old", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OnboardingTokenException(OnboardingTokenFailure.Expired));
        harness.Navigator
            .Setup(n => n.ShowSignUpExpiredAsync(OnboardingTokenFailure.Expired, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await harness.Flow.HandleAppLinkAsync(new Uri("southbaysoccer://auth/register?token=old"), CancellationToken.None);

        harness.Navigator.VerifyAll();
    }

    [Fact]
    public async Task HandleLoginLink_WithPendingSignIn_CompletesSignInWithRememberDevice()
    {
        var harness = new Harness();
        var pending = new PhoneSignInStartResponse(true, "+1 (555) ••• 9421", "Ada Okafor", null);
        harness.Navigator
            .Setup(n => n.ShowSignInVerifyAsync(pending, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await harness.Flow.BeginSignInVerificationAsync(pending, CancellationToken.None);
        harness.Flow.RememberDevice = false;
        harness.Client
            .Setup(c => c.CompleteLoginAsync("tok", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tokens);
        harness.Coordinator
            .Setup(c => c.CompleteSignInAsync(Tokens, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await harness.Flow.HandleAppLinkAsync(new Uri("southbaysoccer://auth/login?token=tok"), CancellationToken.None);

        harness.Coordinator.VerifyAll();
        harness.Flow.PendingSignIn.Should().BeNull();
    }

    [Fact]
    public async Task HandleLoginLink_TokenForAnotherUser_ShowsGenericErrorAndReturnsToWelcome()
    {
        var harness = new Harness();
        var pending = new PhoneSignInStartResponse(true, "+1 (555) ••• 9421", "Ada Okafor", null);
        harness.Navigator
            .Setup(n => n.ShowSignInVerifyAsync(pending, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await harness.Flow.BeginSignInVerificationAsync(pending, CancellationToken.None);
        harness.Client
            .Setup(c => c.CompleteLoginAsync("tok", true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OnboardingTokenException(OnboardingTokenFailure.Mismatch));
        harness.Dialog
            .Setup(d => d.ShowAlertAsync(OnboardingFlow.VerificationFailedTitle, OnboardingFlow.VerificationFailedMessage, "OK", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        harness.Navigator
            .Setup(n => n.PopToWelcomeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await harness.Flow.HandleAppLinkAsync(new Uri("southbaysoccer://auth/login?token=tok"), CancellationToken.None);

        harness.Coordinator.Verify(c => c.CompleteSignInAsync(It.IsAny<AuthenticationTokensResponse>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Dialog.VerifyAll();
        harness.Navigator.VerifyAll();
    }

    [Fact]
    public async Task HandleLoginLink_WithoutPendingSignIn_IsRejectedWithoutTokens()
    {
        var harness = new Harness();
        harness.Dialog
            .Setup(d => d.ShowAlertAsync(OnboardingFlow.VerificationFailedTitle, OnboardingFlow.VerificationFailedMessage, "OK", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await harness.Flow.HandleAppLinkAsync(new Uri("southbaysoccer://auth/login?token=tok"), CancellationToken.None);

        harness.Client.VerifyNoOtherCalls();
        harness.Dialog.VerifyAll();
    }

    [Fact]
    public async Task HandlePastedLink_FindsLinkInsideBotReplyText()
    {
        var harness = new Harness();
        harness.Client
            .Setup(c => c.ValidateRegistrationAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationTokenValidationResponse("+1 (555) ••• 9421"));
        harness.Navigator
            .Setup(n => n.ShowSignUpDetailsAsync("abc", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handled = await harness.Flow.HandlePastedLinkAsync(
            "Welcome! Finish here: https://n9jabay.app/register?token=abc (expires in 15 min)",
            CancellationToken.None);

        handled.Should().BeTrue();
        (await harness.Flow.HandlePastedLinkAsync("   ", CancellationToken.None)).Should().BeFalse();
        (await harness.Flow.HandlePastedLinkAsync("no link here", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task HandleAppLink_SameLinkDeliveredTwice_RedeemsOnce()
    {
        var harness = new Harness();
        harness.Client
            .Setup(c => c.ValidateRegistrationAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationTokenValidationResponse("+1 (555) ••• 9421"));
        harness.Navigator
            .Setup(n => n.ShowSignUpDetailsAsync("abc", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var link = new Uri("southbaysoccer://auth/register?token=abc");

        await harness.Flow.HandleAppLinkAsync(link, CancellationToken.None);
        var second = await harness.Flow.HandleAppLinkAsync(link, CancellationToken.None);

        second.Should().BeTrue();
        harness.Client.Verify(c => c.ValidateRegistrationAsync("abc", It.IsAny<CancellationToken>()), Times.Once);
        harness.Navigator.Verify(n => n.ShowSignUpDetailsAsync("abc", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAppLink_SameLoginLinkDeliveredTwice_CompletesOnce()
    {
        var harness = new Harness();
        var pending = new PhoneSignInStartResponse(true, "+1 (555) ••• 9421", "Ada Okafor", null);
        harness.Navigator.Setup(n => n.ShowSignInVerifyAsync(pending, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await harness.Flow.BeginSignInVerificationAsync(pending, CancellationToken.None);
        harness.Client.Setup(c => c.CompleteLoginAsync("tok", true, It.IsAny<CancellationToken>())).ReturnsAsync(Tokens);
        harness.Coordinator.Setup(c => c.CompleteSignInAsync(Tokens, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var link = new Uri("southbaysoccer://auth/login?token=tok");

        await harness.Flow.HandleAppLinkAsync(link, CancellationToken.None);
        await harness.Flow.HandleAppLinkAsync(link, CancellationToken.None);

        harness.Client.Verify(c => c.CompleteLoginAsync("tok", true, It.IsAny<CancellationToken>()), Times.Once);
        harness.Dialog.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAppLink_TransportFailure_AlertsAndLeavesLinkRetryable()
    {
        var harness = new Harness();
        harness.Client
            .SetupSequence(c => c.ValidateRegistrationAsync("abc", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("timeout"))
            .ReturnsAsync(new RegistrationTokenValidationResponse("+1 (555) ••• 9421"));
        harness.Dialog
            .Setup(d => d.ShowAlertAsync("Sign-up unavailable", OnboardingFlow.RegistrationFailedMessage, "OK", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        harness.Navigator
            .Setup(n => n.ShowSignUpDetailsAsync("abc", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var link = new Uri("southbaysoccer://auth/register?token=abc");

        await harness.Flow.HandleAppLinkAsync(link, CancellationToken.None);
        await harness.Flow.HandleAppLinkAsync(link, CancellationToken.None);

        harness.Dialog.VerifyAll();
        harness.Navigator.Verify(n => n.ShowSignUpDetailsAsync("abc", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAppLink_WhileAuthenticated_IsSwallowed()
    {
        var harness = new Harness();
        harness.Coordinator.SetupGet(c => c.IsAuthenticated).Returns(true);

        var handled = await harness.Flow.HandleAppLinkAsync(new Uri("southbaysoccer://auth/register?token=abc"), CancellationToken.None);

        handled.Should().BeTrue();
        harness.Client.VerifyNoOtherCalls();
        harness.Navigator.VerifyNoOtherCalls();
    }

    private sealed class Harness
    {
        public Mock<IOnboardingClient> Client { get; } = new(MockBehavior.Strict);
        public Mock<IAuthenticationCoordinator> Coordinator { get; } = new(MockBehavior.Strict);
        public Mock<IOnboardingNavigator> Navigator { get; } = new(MockBehavior.Strict);
        public Mock<IExternalLauncher> Launcher { get; } = new(MockBehavior.Strict);
        public Mock<IUserDialogService> Dialog { get; } = new(MockBehavior.Strict);
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        public OnboardingFlow Flow { get; }

        public Harness()
        {
            Coordinator.SetupGet(c => c.IsAuthenticated).Returns(false);
            Flow = new OnboardingFlow(
                Client.Object,
                Coordinator.Object,
                Navigator.Object,
                Launcher.Object,
                Dialog.Object,
                new PickupPalOptions(),
                Time);
        }
    }
}
