using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SouthBaySoccer.Client.Tests.TestSupport;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Client.Tests;

public class LinkWaitingPageModelTests
{
    [Fact]
    public void Tick_CountsDownFromLinkRequestTimeAndExpiresAtFifteenMinutes()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        var flow = new Mock<IOnboardingFlow>();
        flow.SetupGet(f => f.LinkRequestedAt).Returns(time.GetUtcNow());
        flow.SetupGet(f => f.RegisterMessage).Returns("!!register source=n9jabay");
        var pageModel = Create(flow, time);
        pageModel.Initialize(OnboardingLinkKind.Register);

        time.Advance(TimeSpan.FromSeconds(28));
        pageModel.Tick();
        pageModel.RemainingText.Should().Be("14:32");
        pageModel.RemainingPercent.Should().Be(97);
        pageModel.HasExpired.Should().BeFalse();

        time.Advance(TimeSpan.FromMinutes(15));
        pageModel.Tick();
        pageModel.RemainingText.Should().Be("0:00");
        pageModel.RemainingPercent.Should().Be(0);
        pageModel.HasExpired.Should().BeTrue();
    }

    [Fact]
    public void Initialize_ShowsWhatsAppHelpWhenHandoffFailed()
    {
        var flow = new Mock<IOnboardingFlow>();
        flow.SetupGet(f => f.LastHandoffFailed).Returns(true);
        flow.SetupGet(f => f.LoginMessage).Returns("!!login source=n9jabay");
        var pageModel = Create(flow, new FakeTimeProvider());

        pageModel.Initialize(OnboardingLinkKind.Login);

        pageModel.StatusMessage.Should().Be(LinkWaitingPageModel.WhatsAppUnavailableMessage);
        pageModel.IsRegister.Should().BeFalse();
        pageModel.NoReplyHint.Should().Contain("!!login source=n9jabay");
    }

    [Fact]
    public async Task PasteLink_Unrecognized_ShowsMessage()
    {
        var flow = new Mock<IOnboardingFlow>();
        flow.Setup(f => f.HandlePastedLinkAsync("junk", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var clipboard = new Mock<IClipboardReader>();
        clipboard.Setup(c => c.GetTextAsync(It.IsAny<CancellationToken>())).ReturnsAsync("junk");
        var pageModel = Create(flow, new FakeTimeProvider(), clipboard);

        await pageModel.PasteLinkCommand.ExecuteAsync(null);

        pageModel.StatusMessage.Should().Be(LinkWaitingPageModel.PasteFailedMessage);
    }

    // Regression test: the waiting screen used to have no visible input at all — tapping "Paste
    // the link instead" only ever read the OS clipboard, with no field to type or paste into if
    // that came back empty or unrelated. The link must now be taken from the visible text field
    // whenever the player has entered one, and the clipboard read must not even run in that case.
    [Fact]
    public async Task PasteLink_WithTypedText_UsesTheEnteredTextWithoutReadingTheClipboard()
    {
        const string link = "https://n9jabay.desolatravels.com/register?token=abc";
        var flow = new Mock<IOnboardingFlow>();
        flow.Setup(f => f.HandlePastedLinkAsync(link, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var clipboard = new Mock<IClipboardReader>(MockBehavior.Strict);
        var pageModel = Create(flow, new FakeTimeProvider(), clipboard);
        pageModel.PastedLinkText = link;

        await pageModel.PasteLinkCommand.ExecuteAsync(null);

        clipboard.VerifyNoOtherCalls();
        pageModel.HasStatusMessage.Should().BeFalse();
        pageModel.PastedLinkText.Should().BeEmpty("the field clears once the link is accepted");
    }

    [Fact]
    public async Task PasteLink_WithBlankTypedText_FallsBackToTheClipboard()
    {
        var flow = new Mock<IOnboardingFlow>();
        flow.Setup(f => f.HandlePastedLinkAsync("from-clipboard", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var clipboard = new Mock<IClipboardReader>();
        clipboard.Setup(c => c.GetTextAsync(It.IsAny<CancellationToken>())).ReturnsAsync("from-clipboard");
        var pageModel = Create(flow, new FakeTimeProvider(), clipboard);
        pageModel.PastedLinkText = "   ";

        await pageModel.PasteLinkCommand.ExecuteAsync(null);

        clipboard.Verify(c => c.GetTextAsync(It.IsAny<CancellationToken>()), Times.Once);
        pageModel.HasStatusMessage.Should().BeFalse();
    }

    private static LinkWaitingPageModel Create(Mock<IOnboardingFlow> flow, FakeTimeProvider time, Mock<IClipboardReader>? clipboard = null) =>
        new(
            flow.Object,
            new Mock<IOnboardingNavigator>().Object,
            (clipboard ?? new Mock<IClipboardReader>()).Object,
            new ControlledPollingDelay(),
            time);
}
