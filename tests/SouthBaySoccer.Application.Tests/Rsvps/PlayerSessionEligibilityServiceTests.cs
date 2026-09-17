using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Features.Rsvps;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Rsvps;

/// <summary>
/// Product decision (2026-09-16): no waiver requirement. Eligibility is the payment verdict alone;
/// a player with no waiver acceptance on file can RSVP, be promoted, and check in.
/// </summary>
public sealed class PlayerSessionEligibilityServiceTests
{
    [Fact]
    public async Task CheckAsync_WhenPaymentEligible_ReturnsEligibleWithoutConsultingWaivers()
    {
        var playerProfileId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var payment = new Mock<IPaymentEligibilityService>();
        payment
            .Setup(x => x.CheckAsync(playerProfileId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentEligibilityResult(true, null));
        var service = new PlayerSessionEligibilityService(payment.Object);

        var result = await service.CheckAsync(playerProfileId, sessionId);

        result.IsEligible.Should().BeTrue();
        result.Reason.Should().BeNull();
        typeof(PlayerSessionEligibilityService).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.Name)
            .Should().NotContain("IWaiverRepository", "the waiver gate was removed from RSVP eligibility");
    }

    [Fact]
    public async Task CheckAsync_WhenPaymentIneligible_ReturnsPaymentReason()
    {
        var payment = new Mock<IPaymentEligibilityService>();
        payment
            .Setup(x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentEligibilityResult(false, "Membership lapsed."));
        var service = new PlayerSessionEligibilityService(payment.Object);

        var result = await service.CheckAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsEligible.Should().BeFalse();
        result.Reason.Should().Be("Membership lapsed.");
    }

    [Fact]
    public async Task CheckManyAsync_WhenCandidatesGiven_ReturnsOnePaymentVerdictPerDistinctPlayer()
    {
        var eligiblePlayer = Guid.NewGuid();
        var lapsedPlayer = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var payment = new Mock<IPaymentEligibilityService>();
        payment
            .Setup(x => x.CheckAsync(eligiblePlayer, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentEligibilityResult(true, null));
        payment
            .Setup(x => x.CheckAsync(lapsedPlayer, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentEligibilityResult(false, "Membership lapsed."));
        var service = new PlayerSessionEligibilityService(payment.Object);

        var results = await service.CheckManyAsync([eligiblePlayer, lapsedPlayer, eligiblePlayer], sessionId);

        results.Should().HaveCount(2);
        results[eligiblePlayer].Should().BeTrue();
        results[lapsedPlayer].Should().BeFalse();
        payment.Verify(x => x.CheckAsync(eligiblePlayer, sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckManyAsync_WhenNoCandidates_ReturnsEmpty()
    {
        var service = new PlayerSessionEligibilityService(Mock.Of<IPaymentEligibilityService>());

        var results = await service.CheckManyAsync([], Guid.NewGuid());

        results.Should().BeEmpty();
    }
}
