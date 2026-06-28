using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Players;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Players;

public sealed class PlayerProfileCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenUpdatingMyProfile_UpdatesProfileAndStoresMaskedEmergencyContact()
    {
        var identityUserId = Guid.NewGuid();
        var profile = new PlayerProfile
        {
            Id = Guid.NewGuid(),
            IdentityUserId = identityUserId,
            DisplayName = "Old Name",
            NormalizedDisplayName = "OLD NAME",
            PreferredPosition = "Midfielder",
            Role = PlayerRole.Player,
        };
        var repository = new Mock<IPlayerProfileRepository>();
        repository
            .Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        repository
            .Setup(x => x.FindEmergencyContactAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmergencyContact?)null);
        EmergencyContact? capturedContact = null;
        repository
            .Setup(x => x.AddEmergencyContactAsync(It.IsAny<EmergencyContact>(), It.IsAny<CancellationToken>()))
            .Callback<EmergencyContact, CancellationToken>((contact, _) => capturedContact = contact)
            .Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var handler = new UpdateMyProfileCommandHandler(
            CreateCurrentUser(identityUserId).Object,
            new UpdateMyProfileCommandValidator(),
            repository.Object,
            unitOfWork.Object);

        var result = await handler.HandleAsync(
            new UpdateMyProfileCommand(
                "  Tobi  ",
                "  Forward ",
                " https://example.test/photo.png ",
                new EmergencyContactModel(" Desola ", "+1 (310) 555-0123", " Sister ")));

        profile.DisplayName.Should().Be("Tobi");
        profile.NormalizedDisplayName.Should().Be("TOBI");
        profile.PreferredPosition.Should().Be("Forward");
        profile.PhotoUri.Should().Be("https://example.test/photo.png");
        capturedContact.Should().NotBeNull();
        capturedContact!.PlayerProfileId.Should().Be(profile.Id);
        capturedContact.Name.Should().Be("Desola");
        capturedContact.PhoneNumberHash.Should().NotBe("+1 (310) 555-0123");
        capturedContact.MaskedPhoneNumber.Should().NotContain("5550123");
        capturedContact.Relationship.Should().Be("Sister");
        result.DisplayName.Should().Be("Tobi");
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenCreatingGuestProfile_CreatesGuestWithoutIdentityUser()
    {
        PlayerProfile? capturedProfile = null;
        var repository = new Mock<IPlayerProfileRepository>();
        repository
            .Setup(x => x.AddAsync(It.IsAny<PlayerProfile>(), It.IsAny<CancellationToken>()))
            .Callback<PlayerProfile, CancellationToken>((profile, _) => capturedProfile = profile)
            .Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var handler = new CreateGuestProfileCommandHandler(
            new CreateGuestProfileCommandValidator(),
            repository.Object,
            unitOfWork.Object);

        var result = await handler.HandleAsync(
            new CreateGuestProfileCommand(" Tunde ", " Winger ", null, null));

        capturedProfile.Should().NotBeNull();
        capturedProfile!.IdentityUserId.Should().BeNull();
        capturedProfile.IsGuest.Should().BeTrue();
        capturedProfile.Role.Should().Be(PlayerRole.Guest);
        capturedProfile.DisplayName.Should().Be("Tunde");
        capturedProfile.NormalizedDisplayName.Should().Be("TUNDE");
        result.IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenMergingGuestProfile_RetiresGuestAndCreatesCompletedAuditRecord()
    {
        var now = new DateTime(2026, 6, 26, 23, 0, 0, DateTimeKind.Utc);
        var actorUserId = Guid.NewGuid();
        var guest = new PlayerProfile { Id = Guid.NewGuid(), IsGuest = true, Role = PlayerRole.Guest };
        var target = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), Role = PlayerRole.Player };
        ProfileMerge? capturedMerge = null;
        var repository = new Mock<IPlayerProfileRepository>();
        repository.Setup(x => x.FindProfileAsync(guest.Id, It.IsAny<CancellationToken>())).ReturnsAsync(guest);
        repository.Setup(x => x.FindProfileAsync(target.Id, It.IsAny<CancellationToken>())).ReturnsAsync(target);
        repository
            .Setup(x => x.AddProfileMergeAsync(It.IsAny<ProfileMerge>(), It.IsAny<CancellationToken>()))
            .Callback<ProfileMerge, CancellationToken>((merge, _) => capturedMerge = merge)
            .Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(now);
        var handler = new CreateProfileMergeCommandHandler(
            CreateCurrentUser(actorUserId).Object,
            clock.Object,
            new CreateProfileMergeCommandValidator(),
            repository.Object,
            unitOfWork.Object);

        var result = await handler.HandleAsync(new CreateProfileMergeCommand(guest.Id, target.Id));

        guest.IsDeleted.Should().BeTrue();
        capturedMerge.Should().NotBeNull();
        capturedMerge!.Status.Should().Be(ProfileMergeStatus.Completed);
        capturedMerge.MergedAtUtc.Should().Be(now);
        capturedMerge.MergedByActorType.Should().Be(AuditActorType.User);
        capturedMerge.MergedByActorId.Should().Be(actorUserId.ToString("D"));
        result.Status.Should().Be(ProfileMergeStatus.Completed.ToString());
    }

    private static Mock<ICurrentUser> CreateCurrentUser(Guid identityUserId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(identityUserId);
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        return currentUser;
    }
}
