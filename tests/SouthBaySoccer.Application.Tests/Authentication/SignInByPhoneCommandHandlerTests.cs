using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Features.Authentication;

namespace SouthBaySoccer.Application.Tests.Authentication;

public sealed class SignInByPhoneCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenPickupPalUserExists_IssuesSouthBaySoccerTokens()
    {
        var pickupPalUser = new PickupPalUser(
            "cmnddr1ol000ecavpt108stw7",
            "player@pickuppal.test",
            "15106949421",
            "Vic",
            "A",
            null,
            null,
            Array.Empty<string>(),
            DateTime.UtcNow);
        var subject = new AuthenticationTokenSubject(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new[] { "Player" });
        var tokens = new AuthenticationTokenSet(
            "access",
            "refresh",
            DateTime.UtcNow.AddMinutes(15));
        var pickupPalClient = new Mock<IPickupPalUserClient>();
        pickupPalClient
            .Setup(x => x.FindByPhoneAsync("15106949421", It.IsAny<CancellationToken>()))
            .ReturnsAsync(pickupPalUser);
        var syncService = new Mock<IPickupPalUserSyncService>();
        syncService
            .Setup(x => x.SyncAsync(pickupPalUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subject);
        var tokenIssuer = new Mock<IAuthenticationTokenIssuer>();
        tokenIssuer
            .Setup(x => x.IssueTokensAsync(subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
        var handler = new SignInByPhoneCommandHandler(
            new SignInByPhoneCommandValidator(),
            pickupPalClient.Object,
            syncService.Object,
            tokenIssuer.Object);

        var result = await handler.HandleAsync(new SignInByPhoneCommand("+1 (510) 694-9421"));

        result.Should().Be(tokens);
        tokenIssuer.Verify(x => x.IssueTokensAsync(subject, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenPickupPalUserMissing_DoesNotIssueTokens()
    {
        var pickupPalClient = new Mock<IPickupPalUserClient>();
        pickupPalClient
            .Setup(x => x.FindByPhoneAsync("15106949421", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PickupPalUser?)null);
        var tokenIssuer = new Mock<IAuthenticationTokenIssuer>(MockBehavior.Strict);
        var handler = new SignInByPhoneCommandHandler(
            new SignInByPhoneCommandValidator(),
            pickupPalClient.Object,
            Mock.Of<IPickupPalUserSyncService>(),
            tokenIssuer.Object);

        var act = () => handler.HandleAsync(new SignInByPhoneCommand("+1 (510) 694-9421"));

        await act.Should().ThrowAsync<PickupPalUserNotFoundException>();
        tokenIssuer.VerifyNoOtherCalls();
    }
}

