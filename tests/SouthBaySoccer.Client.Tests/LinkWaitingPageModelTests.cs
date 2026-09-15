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
        flow.SetupGet(f => f.RegisterMessage).Returns("!!register n9jabay");
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
        flow.SetupGet(f => f.LoginMessage).Returns("!!login n9jabay");
        var pageModel = Create(flow, new FakeTimeProvider());

        pageModel.Initialize(OnboardingLinkKind.Login);

        pageModel.StatusMessage.Should().Be(LinkWaitingPageModel.WhatsAppUnavailableMessage);
        pageModel.IsRegister.Should().BeFalse();
        pageModel.NoReplyHint.Should().Contain("!!login n9jabay");
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

    private static LinkWaitingPageModel Create(Mock<IOnboardingFlow> flow, FakeTimeProvider time, Mock<IClipboardReader>? clipboard = null) =>
        new(
            flow.Object,
            new Mock<IOnboardingNavigator>().Object,
            (clipboard ?? new Mock<IClipboardReader>()).Object,
            new ControlledPollingDelay(),
            time);
}
