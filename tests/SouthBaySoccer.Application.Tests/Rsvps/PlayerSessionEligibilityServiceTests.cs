using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Domain.Enumerations;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Rsvps;

/// <summary>
/// Product decision (2026-09-16): no waiver requirement. Interactive eligibility checks payment; promotions also require current group approval;
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
        var service = CreateService(payment.Object);

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
        var service = CreateService(payment.Object);

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
        var service = CreateService(payment.Object);

        var results = await service.CheckManyAsync([eligiblePlayer, lapsedPlayer, eligiblePlayer], sessionId);

        results.Should().HaveCount(2);
        results[eligiblePlayer].Should().BeTrue();
        results[lapsedPlayer].Should().BeFalse();
        payment.Verify(x => x.CheckAsync(eligiblePlayer, sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckManyAsync_WhenNoCandidates_ReturnsEmpty()
    {
        var service = CreateService(Mock.Of<IPaymentEligibilityService>());

        var results = await service.CheckManyAsync([], Guid.NewGuid());

        results.Should().BeEmpty();
    }
    [Fact]
    public async Task CheckManyAsync_WhenMembershipEnded_OnlyApprovedEligibleCandidateCanBePromoted()
    {
        var session = new Session { Id = Guid.NewGuid(), GroupChatId = Guid.NewGuid() };
        var removed = Guid.NewGuid();
        var approved = Guid.NewGuid();
        var unpaid = Guid.NewGuid();
        var sessions = new Mock<ISessionRepository>();
        sessions.Setup(x => x.GetByIdAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var memberships = new Mock<IPlayerGroupLinkRepository>();
        memberships.Setup(x => x.ListApprovedPlayerIdsAsync(session.GroupChatId.Value,
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([approved, unpaid]);
        var payment = new Mock<IPaymentEligibilityService>();
        payment.Setup(x => x.CheckAsync(approved, session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentEligibilityResult(true, null));
        payment.Setup(x => x.CheckAsync(unpaid, session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentEligibilityResult(false, "Payment required."));
        var service = new PlayerSessionEligibilityService(payment.Object, sessions.Object, memberships.Object);

        var results = await service.CheckManyAsync([removed, approved, unpaid, removed], session.Id);

        results.Should().HaveCount(3);
        results[removed].Should().BeFalse();
        results[approved].Should().BeTrue();
        results[unpaid].Should().BeFalse();
        payment.Verify(x => x.CheckAsync(removed, session.Id, It.IsAny<CancellationToken>()), Times.Never);
        memberships.Verify(x => x.ListApprovedPlayerIdsAsync(session.GroupChatId.Value,
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckManyAsync_WhenSessionMissingOrImportedGroupUnresolved_DeniesPromotion(bool sessionExists)
    {
        var sessions = new Mock<ISessionRepository>();
        sessions.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionExists ? new Session { PickupPalOrigin = PickupPalOrigin.Imported } : null);
        var payment = new Mock<IPaymentEligibilityService>(MockBehavior.Strict);
        var service = new PlayerSessionEligibilityService(payment.Object, sessions.Object, Mock.Of<IPlayerGroupLinkRepository>());
        var playerId = Guid.NewGuid();

        var results = await service.CheckManyAsync([playerId], Guid.NewGuid());

        results[playerId].Should().BeFalse();
    }

    private static PlayerSessionEligibilityService CreateService(IPaymentEligibilityService payment)
    {
        var sessions = new Mock<ISessionRepository>();
        sessions.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Session { Id = Guid.NewGuid() });
        return new PlayerSessionEligibilityService(payment, sessions.Object, Mock.Of<IPlayerGroupLinkRepository>());
    }
}
