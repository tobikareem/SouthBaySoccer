using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Client.Tests;

public class SignUpWelcomePageModelTests
{
    private static readonly AuthenticationTokensResponse Tokens =
        new("a", "r", new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task Continue_WithRegistration_HandsTokensToCoordinatorOnceAndClearsThem()
    {
        var coordinator = new Mock<IAuthenticationCoordinator>(MockBehavior.Strict);
        coordinator.Setup(c => c.CompleteSignInAsync(Tokens, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var pageModel = new SignUpWelcomePageModel(coordinator.Object);
        pageModel.Initialize(new RegistrationCompletedResponse(Tokens, "Ada", "+1 (555) ••• 9421", ["Saturday crew", "Sunday 7s"], false));

        await pageModel.ContinueCommand.ExecuteAsync(null);
        await pageModel.ContinueCommand.ExecuteAsync(null);

        coordinator.Verify(c => c.CompleteSignInAsync(Tokens, It.IsAny<CancellationToken>()), Times.Once);
        pageModel.Heading.Should().Be("You're in, Ada.");
        pageModel.HasGroup.Should().BeTrue();
        pageModel.GroupDetail.Should().Contain("plus 1 more");
        pageModel.HistoryPending.Should().BeFalse();
    }

    [Fact]
    public async Task Continue_CoordinatorFails_IsRecoverableAndKeepsTokensForRetry()
    {
        var coordinator = new Mock<IAuthenticationCoordinator>(MockBehavior.Strict);
        coordinator
            .SetupSequence(c => c.CompleteSignInAsync(Tokens, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("window not ready"))
            .Returns(Task.CompletedTask);
        var pageModel = new SignUpWelcomePageModel(coordinator.Object);
        pageModel.Initialize(new RegistrationCompletedResponse(Tokens, "Ada", "+1 (555) ••• 9421", [], true));

        await pageModel.ContinueCommand.ExecuteAsync(null);
        pageModel.StatusMessage.Should().Be(SignUpWelcomePageModel.ContinueFailedMessage);
        pageModel.HasNoGroup.Should().BeTrue();

        await pageModel.ContinueCommand.ExecuteAsync(null);
        coordinator.Verify(c => c.CompleteSignInAsync(Tokens, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
