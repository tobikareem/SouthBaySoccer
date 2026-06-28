using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Waivers;
using SouthBaySoccer.Domain.Entities.Compliance;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Waivers;

public sealed class WaiverCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenCurrentWaiverNotAccepted_CreatesAcceptanceForSignedInProfile()
    {
        var now = new DateTime(2026, 6, 26, 23, 30, 0, DateTimeKind.Utc);
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var waiver = new WaiverDocument
        {
            Id = Guid.NewGuid(),
            Version = "v2",
            Title = "Pickup Soccer Waiver",
            ContentHash = "hash-v2",
            Status = WaiverDocumentStatus.Published,
            PublishedAtUtc = now.AddDays(-1),
        };
        WaiverAcceptance? capturedAcceptance = null;
        var profileRepository = new Mock<IPlayerProfileRepository>();
        profileRepository
            .Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        var waiverRepository = new Mock<IWaiverRepository>();
        waiverRepository.Setup(x => x.GetCurrentPublishedWaiverAsync(It.IsAny<CancellationToken>())).ReturnsAsync(waiver);
        waiverRepository
            .Setup(x => x.FindAcceptanceAsync(profile.Id, waiver.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaiverAcceptance?)null);
        waiverRepository
            .Setup(x => x.AddAcceptanceAsync(It.IsAny<WaiverAcceptance>(), It.IsAny<CancellationToken>()))
            .Callback<WaiverAcceptance, CancellationToken>((acceptance, _) => capturedAcceptance = acceptance)
            .Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(now);
        var handler = new AcceptCurrentWaiverCommandHandler(
            CreateCurrentUser(identityUserId).Object,
            clock.Object,
            profileRepository.Object,
            waiverRepository.Object,
            unitOfWork.Object);

        var result = await handler.HandleAsync(new AcceptCurrentWaiverCommand());

        capturedAcceptance.Should().NotBeNull();
        capturedAcceptance!.PlayerProfileId.Should().Be(profile.Id);
        capturedAcceptance.WaiverDocumentId.Should().Be(waiver.Id);
        capturedAcceptance.AcceptedAtUtc.Should().Be(now);
        capturedAcceptance.ContentHash.Should().Be("hash-v2");
        result.Version.Should().Be("v2");
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenCurrentWaiverAlreadyAccepted_ReturnsExistingAcceptanceWithoutSaving()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = identityUserId };
        var waiver = new WaiverDocument { Id = Guid.NewGuid(), Version = "v1", Title = "Waiver", ContentHash = "hash" };
        var acceptedAtUtc = new DateTime(2026, 6, 25, 23, 30, 0, DateTimeKind.Utc);
        var acceptance = new WaiverAcceptance
        {
            Id = Guid.NewGuid(),
            PlayerProfileId = profile.Id,
            WaiverDocumentId = waiver.Id,
            AcceptedAtUtc = acceptedAtUtc,
            ContentHash = waiver.ContentHash,
        };
        var profileRepository = new Mock<IPlayerProfileRepository>();
        profileRepository
            .Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        var waiverRepository = new Mock<IWaiverRepository>();
        waiverRepository.Setup(x => x.GetCurrentPublishedWaiverAsync(It.IsAny<CancellationToken>())).ReturnsAsync(waiver);
        waiverRepository
            .Setup(x => x.FindAcceptanceAsync(profile.Id, waiver.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(acceptance);
        var unitOfWork = new Mock<IUnitOfWork>();
        var handler = new AcceptCurrentWaiverCommandHandler(
            CreateCurrentUser(identityUserId).Object,
            Mock.Of<IClock>(),
            profileRepository.Object,
            waiverRepository.Object,
            unitOfWork.Object);

        var result = await handler.HandleAsync(new AcceptCurrentWaiverCommand());

        result.WaiverAcceptanceId.Should().Be(acceptance.Id);
        result.AcceptedAtUtc.Should().Be(acceptedAtUtc);
        waiverRepository.Verify(x => x.AddAcceptanceAsync(It.IsAny<WaiverAcceptance>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<ICurrentUser> CreateCurrentUser(Guid identityUserId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(identityUserId);
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        return currentUser;
    }
}
