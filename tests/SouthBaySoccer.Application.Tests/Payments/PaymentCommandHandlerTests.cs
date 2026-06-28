using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Payments;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Payments;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Payments;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Payments;

public sealed class PaymentCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenStripeCustomerReferenceExists_CreatesDropInCheckoutWithoutWritingEligibility()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId, DisplayName = "Ada" };
        var session = new Session { Id = Guid.NewGuid(), Title = "Pickup" };
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(identityUserId);
        var profiles = new Mock<IPlayerProfileRepository>();
        profiles.Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        var sessions = new Mock<ISessionRepository>();
        sessions.Setup(x => x.GetByIdAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.FindStripeCustomerReferenceAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeCustomerReference { PlayerProfileId = profile.Id, StripeCustomerId = "cus_123" });
        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(x => x.CreateCheckoutSessionAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutSessionModel("https://checkout.stripe.test/session", "cs_test", DateTime.UtcNow.AddMinutes(30)));
        var handler = new CreateSessionDropInCheckoutCommandHandler(
            currentUser.Object,
            profiles.Object,
            sessions.Object,
            payments.Object,
            gateway.Object,
            new CreateSessionDropInCheckoutCommandValidator());

        var result = await handler.HandleAsync(new CreateSessionDropInCheckoutCommand(session.Id, "/paid", "/cancel"));

        result.SessionId.Should().Be(session.Id);
        result.ProviderSessionId.Should().Be("cs_test");
        gateway.Verify(x => x.CreateCheckoutSessionAsync(
            It.Is<CheckoutSessionRequest>(r => r.SessionId == session.Id && r.PlayerProfileId == profile.Id && r.Mode == "payment" && r.PriceCode == "drop-in"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckAsync_WhenNoMembershipOrDropIn_ReturnsPaymentRequired()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTime.UtcNow);
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(x => x.GetEligibilityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentEligibilityProjection(false, false, null));
        var service = new PaymentProjectionEligibilityService(clock.Object, payments.Object);

        var result = await service.CheckAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsEligible.Should().BeFalse();
        result.Reason.Should().Be("Payment required.");
    }
}