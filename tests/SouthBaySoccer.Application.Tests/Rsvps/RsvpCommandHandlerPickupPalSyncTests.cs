using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Rsvps;

/// <summary>RSVP-9: the handlers push to Pickup Pal only after the local transaction and never let it fail the RSVP.</summary>
public sealed class RsvpCommandHandlerPickupPalSyncTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 16, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SubmitRsvp_WhenLocalWriteSucceeds_SyncsAfterwardsAndReportsTheStatus()
    {
        var context = new TestContext();
        var sequence = new List<string>();
        context.RsvpRepository
            .Setup(x => x.SubmitRsvpAsync(context.Session.Id, context.Profile.Id, RsvpStatus.Going, It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("local"))
            .ReturnsAsync(new RsvpMutationResult(context.Session.Id, context.Profile.Id, RsvpMutationState.Going, Guid.NewGuid()));
        context.SyncService
            .Setup(x => x.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id, It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("sync"))
            .ReturnsAsync(PickupPalSyncStatus.Synced);

        var result = await context.SubmitHandler().HandleAsync(new SubmitRsvpCommand(context.Session.Id, RsvpStatus.Going));

        sequence.Should().Equal("local", "sync");
        result.State.Should().Be("Going");
        result.PickupPalSync.Should().Be(PickupPalSyncStatus.Synced);
    }

    [Fact]
    public async Task SubmitRsvp_WhenSyncReportsPending_RsvpStillSucceedsWithPendingStatus()
    {
        var context = new TestContext();
        context.RsvpRepository
            .Setup(x => x.SubmitRsvpAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RsvpStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RsvpMutationResult(context.Session.Id, context.Profile.Id, RsvpMutationState.Going, Guid.NewGuid()));
        context.SyncService
            .Setup(x => x.SyncAfterLocalWriteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalSyncStatus.Pending);

        var result = await context.SubmitHandler().HandleAsync(new SubmitRsvpCommand(context.Session.Id, RsvpStatus.Going));

        result.PickupPalSync.Should().Be(PickupPalSyncStatus.Pending);
    }

    [Fact]
    public async Task SubmitRsvp_WhenEligibilityFails_NeverSyncs()
    {
        var context = new TestContext(eligible: false);

        var act = () => context.SubmitHandler().HandleAsync(new SubmitRsvpCommand(context.Session.Id, RsvpStatus.Going));

        await act.Should().ThrowAsync<ApplicationConflictException>();
        context.SyncService.Verify(
            x => x.SyncAfterLocalWriteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelRsvp_WhenAPlayerIsPromoted_SyncsBothTheCancellingAndThePromotedPlayer()
    {
        var context = new TestContext();
        var promotedPlayerProfileId = Guid.NewGuid();
        context.RsvpRepository
            .Setup(x => x.CancelAndPromoteAsync(
                context.Session.Id,
                context.Profile.Id,
                It.IsAny<Func<IReadOnlyCollection<Guid>, CancellationToken, Task<IReadOnlyDictionary<Guid, bool>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RsvpMutationResult(
                context.Session.Id,
                context.Profile.Id,
                RsvpMutationState.Canceled,
                PromotedPlayerProfileId: promotedPlayerProfileId));
        context.SyncService
            .Setup(x => x.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalSyncStatus.Synced);
        context.SyncService
            .Setup(x => x.SyncAfterLocalWriteAsync(context.Session.Id, promotedPlayerProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalSyncStatus.Pending);

        var result = await context.CancelHandler().HandleAsync(new CancelRsvpCommand(context.Session.Id));

        result.State.Should().Be("Canceled");
        result.PickupPalSync.Should().Be(PickupPalSyncStatus.Synced, "the response reports the caller's own sync, not the promoted player's");
        context.SyncService.Verify(
            x => x.SyncAfterLocalWriteAsync(context.Session.Id, promotedPlayerProfileId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelRsvp_WhenNobodyIsPromoted_SyncsOnlyTheCancellingPlayer()
    {
        var context = new TestContext();
        context.RsvpRepository
            .Setup(x => x.CancelAndPromoteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Func<IReadOnlyCollection<Guid>, CancellationToken, Task<IReadOnlyDictionary<Guid, bool>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RsvpMutationResult(context.Session.Id, context.Profile.Id, RsvpMutationState.Canceled));
        context.SyncService
            .Setup(x => x.SyncAfterLocalWriteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalSyncStatus.NotApplicable);

        var result = await context.CancelHandler().HandleAsync(new CancelRsvpCommand(context.Session.Id));

        result.PickupPalSync.Should().Be(PickupPalSyncStatus.NotApplicable);
        context.SyncService.Verify(
            x => x.SyncAfterLocalWriteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AdminOverrideRsvp_WhenLocalWriteSucceeds_SyncsTheTargetPlayer()
    {
        var context = new TestContext();
        var targetPlayer = new PlayerProfile { Id = Guid.NewGuid(), DisplayName = "Grace" };
        context.PlayerProfileRepository
            .Setup(x => x.FindProfileAsync(targetPlayer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetPlayer);
        context.RsvpRepository
            .Setup(x => x.AddWithAdminOverrideAsync(context.Session.Id, targetPlayer.Id, context.Profile.Id, "Paid cash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RsvpMutationResult(context.Session.Id, targetPlayer.Id, RsvpMutationState.Going, Guid.NewGuid()));
        context.SyncService
            .Setup(x => x.SyncAfterLocalWriteAsync(context.Session.Id, targetPlayer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalSyncStatus.Synced);

        var result = await context.AdminOverrideHandler().HandleAsync(
            new AdminOverrideRsvpCommand(context.Session.Id, targetPlayer.Id, "Paid cash"));

        result.PickupPalSync.Should().Be(PickupPalSyncStatus.Synced);
        context.SyncService.Verify(
            x => x.SyncAfterLocalWriteAsync(context.Session.Id, targetPlayer.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetMyRsvp_WhenRowCarriesSyncStatus_ReportsIt()
    {
        var context = new TestContext();
        context.RsvpRepository
            .Setup(x => x.GetMyRsvpAsync(context.Session.Id, context.Profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RsvpMutationResult(
                context.Session.Id,
                context.Profile.Id,
                RsvpMutationState.Going,
                Guid.NewGuid(),
                PickupPalSyncStatus: PickupPalSyncStatus.Failed));

        var result = await context.GetMyRsvpHandler().HandleAsync(context.Session.Id);

        result!.PickupPalSync.Should().Be(PickupPalSyncStatus.Failed);
    }

    private sealed class TestContext
    {
        public PlayerProfile Profile { get; } = new()
        {
            Id = Guid.NewGuid(),
            IdentityUserId = Guid.NewGuid(),
            DisplayName = "Ada",
            PickupPalUserId = "pp-user-1",
        };

        public Session Session { get; } = new()
        {
            Id = Guid.NewGuid(),
            SeasonId = Guid.NewGuid(),
            VenueId = Guid.NewGuid(),
            Title = "Fire FC - Tuesday pickup",
            Format = "7v7",
            Capacity = 14,
            TeamCount = 2,
            StartsAtUtc = NowUtc.AddHours(4),
            CheckInOpensAtUtc = NowUtc.AddHours(4).AddMinutes(-10),
            CheckInClosesAtUtc = NowUtc.AddHours(4).AddMinutes(5),
            RsvpDeadlineUtc = NowUtc.AddHours(3),
            OccurrenceKey = "pickuppal:game-1",
            Status = SessionStatus.Published,
        };

        public Mock<IRsvpRepository> RsvpRepository { get; } = new();

        public Mock<IRsvpPickupPalSyncService> SyncService { get; } = new();

        public Mock<IPlayerProfileRepository> PlayerProfileRepository { get; } = new();

        private readonly Mock<ICurrentUser> currentUser = new();
        private readonly Mock<ISessionRepository> sessionRepository = new();
        private readonly Mock<IClock> clock = new();
        private readonly Mock<IPlayerSessionEligibilityService> eligibilityService = new();

        public TestContext(bool eligible = true)
        {
            currentUser.SetupGet(x => x.UserId).Returns(Profile.IdentityUserId);
            PlayerProfileRepository
                .Setup(x => x.FindByIdentityUserIdAsync(Profile.IdentityUserId!.Value, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Profile);
            sessionRepository.Setup(x => x.GetByIdAsync(Session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Session);
            clock.SetupGet(x => x.UtcNow).Returns(NowUtc);
            eligibilityService
                .Setup(x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PlayerSessionEligibilityResult(eligible, eligible ? null : "Payment required."));
        }

        public SubmitRsvpCommandHandler SubmitHandler() =>
            new(
                currentUser.Object,
                clock.Object,
                new SubmitRsvpCommandValidator(),
                PlayerProfileRepository.Object,
                sessionRepository.Object,
                eligibilityService.Object,
                RsvpRepository.Object,
                SyncService.Object);

        public CancelRsvpCommandHandler CancelHandler() =>
            new(
                currentUser.Object,
                clock.Object,
                PlayerProfileRepository.Object,
                sessionRepository.Object,
                eligibilityService.Object,
                RsvpRepository.Object,
                SyncService.Object);

        public AdminOverrideRsvpCommandHandler AdminOverrideHandler() =>
            new(
                currentUser.Object,
                new AdminOverrideRsvpCommandValidator(),
                PlayerProfileRepository.Object,
                sessionRepository.Object,
                RsvpRepository.Object,
                SyncService.Object);

        public GetMyRsvpQueryHandler GetMyRsvpHandler() =>
            new(currentUser.Object, PlayerProfileRepository.Object, RsvpRepository.Object);
    }
}
