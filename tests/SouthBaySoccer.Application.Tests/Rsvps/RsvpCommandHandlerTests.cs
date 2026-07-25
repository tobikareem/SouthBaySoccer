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

public sealed class RsvpCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenEligibilityFails_ThrowsConflictAndDoesNotMutateRsvp()
    {
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = "Ada" };
        var session = FutureSession();
        var eligibilityService = new Mock<IPlayerSessionEligibilityService>();
        eligibilityService
            .Setup(x => x.CheckAsync(profile.Id, session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerSessionEligibilityResult(false, "Waiver required."));
        var rsvpRepository = new Mock<IRsvpRepository>();
        var handler = CreateSubmitHandler(profile, session, eligibilityService.Object, rsvpRepository.Object);

        var act = () => handler.HandleAsync(new SubmitRsvpCommand(session.Id, RsvpStatus.Going));

        await act.Should().ThrowAsync<ApplicationConflictException>();
        rsvpRepository.Verify(
            x => x.SubmitRsvpAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RsvpStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenDeadlinePassed_ThrowsConflictBeforeEligibilityCheck()
    {
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = "Ada" };
        var session = FutureSession();
        session.RsvpDeadlineUtc = Utc(2026, 7, 7, 15, 0);
        var eligibilityService = new Mock<IPlayerSessionEligibilityService>();
        var handler = CreateSubmitHandler(profile, session, eligibilityService.Object, Mock.Of<IRsvpRepository>());

        var act = () => handler.HandleAsync(new SubmitRsvpCommand(session.Id, RsvpStatus.Going));

        await act.Should().ThrowAsync<ApplicationConflictException>();
        eligibilityService.Verify(x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenSessionCanceled_ThrowsConflictBeforeEligibilityCheck()
    {
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = "Ada" };
        var session = FutureSession();
        session.Status = SessionStatus.Canceled;
        var eligibilityService = new Mock<IPlayerSessionEligibilityService>();
        var rsvpRepository = new Mock<IRsvpRepository>();
        var handler = CreateSubmitHandler(profile, session, eligibilityService.Object, rsvpRepository.Object);

        var act = () => handler.HandleAsync(new SubmitRsvpCommand(session.Id, RsvpStatus.Going));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("RSVP is not available for this session.");
        eligibilityService.Verify(
            x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        rsvpRepository.Verify(
            x => x.SubmitRsvpAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RsvpStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenCancelingRsvpOnCanceledSession_ThrowsConflictWithoutMutating()
    {
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = "Ada" };
        var session = FutureSession();
        session.Status = SessionStatus.Canceled;
        var rsvpRepository = new Mock<IRsvpRepository>();
        var handler = CreateCancelHandler(profile, session, rsvpRepository.Object);

        var act = () => handler.HandleAsync(new CancelRsvpCommand(session.Id));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("RSVP is not available for this session.");
        rsvpRepository.Verify(
            x => x.CancelAndPromoteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Func<Guid, CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenEligible_CallsRepositoryAndReturnsWaitlistPosition()
    {
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = "Ada" };
        var session = FutureSession();
        var rsvpRepository = new Mock<IRsvpRepository>();
        rsvpRepository
            .Setup(x => x.SubmitRsvpAsync(session.Id, profile.Id, RsvpStatus.Going, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RsvpMutationResult(session.Id, profile.Id, RsvpMutationState.Waitlisted, WaitlistEntryId: Guid.NewGuid(), WaitlistPosition: 2));
        var handler = CreateSubmitHandler(profile, session, playerSessionEligibilityService: EligibleService(), rsvpRepository.Object);

        var result = await handler.HandleAsync(new SubmitRsvpCommand(session.Id, RsvpStatus.Going));

        result.State.Should().Be(nameof(RsvpMutationState.Waitlisted));
        result.WaitlistPosition.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_WhenCheckInIsInsideWindow_RecordsCheckInWithoutOverride()
    {
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = "Ada" };
        var session = FutureSession();
        var checkIn = new CheckIn
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            PlayerProfileId = profile.Id,
            CheckedInByPlayerProfileId = profile.Id,
            CheckedInAtUtc = Utc(2026, 7, 7, 19, 35),
            Outcome = AttendanceOutcome.CheckedIn
        };
        var rsvpRepository = new Mock<IRsvpRepository>();
        rsvpRepository
            .Setup(x => x.RecordCheckInAsync(session.Id, profile.Id, profile.Id, Utc(2026, 7, 7, 19, 35), AttendanceOutcome.CheckedIn, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckInMutationResult(checkIn));
        var handler = CreateCheckInHandler(profile, session, Utc(2026, 7, 7, 19, 35), rsvpRepository.Object);

        var result = await handler.HandleAsync(new CheckInPlayerCommand(session.Id, profile.Id, AttendanceOutcome.CheckedIn, "not late"));

        result.IsLateOverride.Should().BeFalse();
        result.AdminOverrideId.Should().BeNull();
        rsvpRepository.Verify(
            x => x.RecordCheckInAsync(session.Id, profile.Id, profile.Id, Utc(2026, 7, 7, 19, 35), AttendanceOutcome.CheckedIn, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenCheckInIsOutsideWindowWithoutReason_ThrowsConflictAndDoesNotRecordCheckIn()
    {
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = "Ada" };
        var session = FutureSession();
        var rsvpRepository = new Mock<IRsvpRepository>();
        var handler = CreateCheckInHandler(profile, session, Utc(2026, 7, 7, 19, 50), rsvpRepository.Object);

        var act = () => handler.HandleAsync(new CheckInPlayerCommand(session.Id, profile.Id, AttendanceOutcome.CheckedIn));

        await act.Should().ThrowAsync<ApplicationConflictException>();
        rsvpRepository.Verify(
            x => x.RecordCheckInAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<AttendanceOutcome>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenCheckInIsOutsideWindowWithReason_RecordsLateOverride()
    {
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = "Ada" };
        var session = FutureSession();
        var overrideId = Guid.NewGuid();
        var checkIn = new CheckIn
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            PlayerProfileId = profile.Id,
            CheckedInByPlayerProfileId = profile.Id,
            CheckedInAtUtc = Utc(2026, 7, 7, 19, 50),
            Outcome = AttendanceOutcome.Late
        };
        var rsvpRepository = new Mock<IRsvpRepository>();
        rsvpRepository
            .Setup(x => x.RecordCheckInAsync(session.Id, profile.Id, profile.Id, Utc(2026, 7, 7, 19, 50), AttendanceOutcome.Late, "traffic at gate", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckInMutationResult(checkIn, overrideId, "traffic at gate"));
        var handler = CreateCheckInHandler(profile, session, Utc(2026, 7, 7, 19, 50), rsvpRepository.Object);

        var result = await handler.HandleAsync(new CheckInPlayerCommand(session.Id, profile.Id, AttendanceOutcome.Late, " traffic at gate "));

        result.IsLateOverride.Should().BeTrue();
        result.AdminOverrideId.Should().Be(overrideId);
        result.LateOverrideReason.Should().Be("traffic at gate");
    }

    [Theory]
    [InlineData(19, 30)]
    [InlineData(19, 45)]
    public async Task HandleAsync_WhenSelfCheckInIsAtWindowBoundary_RecordsAuthenticatedPlayer(
        int hour,
        int minute)
    {
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = "Ada" };
        var session = FutureSession();
        var nowUtc = Utc(2026, 7, 7, hour, minute);
        var checkIn = new CheckIn
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            PlayerProfileId = profile.Id,
            CheckedInByPlayerProfileId = profile.Id,
            CheckedInAtUtc = nowUtc,
            Outcome = AttendanceOutcome.CheckedIn
        };
        var rsvpRepository = new Mock<IRsvpRepository>();
        rsvpRepository
            .Setup(x => x.GetGameDayAttendanceAsync(session.Id, profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameDayAttendanceRecord(1, 0, 0, true, false, false, []));
        rsvpRepository
            .Setup(x => x.RecordCheckInAsync(
                session.Id,
                profile.Id,
                profile.Id,
                nowUtc,
                AttendanceOutcome.CheckedIn,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckInMutationResult(checkIn));
        var handler = CreateSelfCheckInHandler(
            profile,
            session,
            nowUtc,
            EligibleService(),
            rsvpRepository.Object);

        var result = await handler.HandleAsync(new SelfCheckInCommand(session.Id));

        result.PlayerProfileId.Should().Be(profile.Id);
        result.CheckedInByPlayerProfileId.Should().Be(profile.Id);
        result.CheckedInAtUtc.Should().Be(nowUtc);
        result.Outcome.Should().Be(nameof(AttendanceOutcome.CheckedIn));
    }

    [Fact]
    public async Task HandleAsync_WhenWaitlistedPlayerSelfChecksIn_RecordsAttendance()
    {
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = "Wade" };
        var session = FutureSession();
        var nowUtc = Utc(2026, 7, 7, 19, 30);
        var checkIn = new CheckIn
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            PlayerProfileId = profile.Id,
            CheckedInByPlayerProfileId = profile.Id,
            CheckedInAtUtc = nowUtc,
            Outcome = AttendanceOutcome.CheckedIn
        };
        var rsvpRepository = new Mock<IRsvpRepository>();
        rsvpRepository
            // IsCurrentPlayerGoing = false, IsCurrentPlayerWaitlisted = true.
            .Setup(x => x.GetGameDayAttendanceAsync(session.Id, profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameDayAttendanceRecord(20, 0, 0, false, true, false, []));
        rsvpRepository
            .Setup(x => x.RecordCheckInAsync(
                session.Id, profile.Id, profile.Id, nowUtc, AttendanceOutcome.CheckedIn, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckInMutationResult(checkIn));
        var handler = CreateSelfCheckInHandler(profile, session, nowUtc, EligibleService(), rsvpRepository.Object);

        var result = await handler.HandleAsync(new SelfCheckInCommand(session.Id));

        result.PlayerProfileId.Should().Be(profile.Id);
        result.Outcome.Should().Be(nameof(AttendanceOutcome.CheckedIn));
        rsvpRepository.Verify(
            x => x.RecordCheckInAsync(
                session.Id, profile.Id, profile.Id, nowUtc, AttendanceOutcome.CheckedIn, null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenSelfCheckInIsOutsideWindow_DoesNotReadAttendanceOrMutate()
    {
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = "Ada" };
        var session = FutureSession();
        var rsvpRepository = new Mock<IRsvpRepository>();
        var handler = CreateSelfCheckInHandler(
            profile,
            session,
            Utc(2026, 7, 7, 19, 46),
            EligibleService(),
            rsvpRepository.Object);

        var act = () => handler.HandleAsync(new SelfCheckInCommand(session.Id));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("Self check-in is outside the session check-in window.");
        rsvpRepository.Verify(
            x => x.GetGameDayAttendanceAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        rsvpRepository.Verify(
            x => x.RecordCheckInAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<AttendanceOutcome>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenSelfCheckInEligibilityFails_DoesNotMutate()
    {
        var profile = new PlayerProfile { Id = Guid.NewGuid(), IdentityUserId = Guid.NewGuid(), DisplayName = "Ada" };
        var session = FutureSession();
        var eligibilityService = new Mock<IPlayerSessionEligibilityService>();
        eligibilityService
            .Setup(x => x.CheckAsync(profile.Id, session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerSessionEligibilityResult(false, "Payment required."));
        var rsvpRepository = new Mock<IRsvpRepository>();
        rsvpRepository
            .Setup(x => x.GetGameDayAttendanceAsync(session.Id, profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameDayAttendanceRecord(1, 0, 0, true, false, false, []));
        var handler = CreateSelfCheckInHandler(
            profile,
            session,
            Utc(2026, 7, 7, 19, 35),
            eligibilityService.Object,
            rsvpRepository.Object);

        var act = () => handler.HandleAsync(new SelfCheckInCommand(session.Id));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("Payment required.");
        rsvpRepository.Verify(
            x => x.RecordCheckInAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<AttendanceOutcome>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static SubmitRsvpCommandHandler CreateSubmitHandler(
        PlayerProfile profile,
        Session session,
        IPlayerSessionEligibilityService playerSessionEligibilityService,
        IRsvpRepository rsvpRepository)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(profile.IdentityUserId);
        var playerProfileRepository = new Mock<IPlayerProfileRepository>();
        playerProfileRepository
            .Setup(x => x.FindByIdentityUserIdAsync(profile.IdentityUserId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Utc(2026, 7, 7, 16, 0));

        return new SubmitRsvpCommandHandler(
            currentUser.Object,
            clock.Object,
            new SubmitRsvpCommandValidator(),
            playerProfileRepository.Object,
            sessionRepository.Object,
            playerSessionEligibilityService,
            rsvpRepository);
    }

    private static CancelRsvpCommandHandler CreateCancelHandler(
        PlayerProfile profile,
        Session session,
        IRsvpRepository rsvpRepository)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(profile.IdentityUserId);
        var playerProfileRepository = new Mock<IPlayerProfileRepository>();
        playerProfileRepository
            .Setup(x => x.FindByIdentityUserIdAsync(profile.IdentityUserId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Utc(2026, 7, 7, 16, 0));

        return new CancelRsvpCommandHandler(
            currentUser.Object,
            clock.Object,
            playerProfileRepository.Object,
            sessionRepository.Object,
            Mock.Of<IPlayerSessionEligibilityService>(),
            rsvpRepository);
    }


    private static CheckInPlayerCommandHandler CreateCheckInHandler(
        PlayerProfile profile,
        Session session,
        DateTime nowUtc,
        IRsvpRepository rsvpRepository)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(profile.IdentityUserId);
        var playerProfileRepository = new Mock<IPlayerProfileRepository>();
        playerProfileRepository
            .Setup(x => x.FindByIdentityUserIdAsync(profile.IdentityUserId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(nowUtc);

        return new CheckInPlayerCommandHandler(
            currentUser.Object,
            clock.Object,
            new CheckInPlayerCommandValidator(),
            playerProfileRepository.Object,
            sessionRepository.Object,
            rsvpRepository);
    }

    private static SelfCheckInCommandHandler CreateSelfCheckInHandler(
        PlayerProfile profile,
        Session session,
        DateTime nowUtc,
        IPlayerSessionEligibilityService eligibilityService,
        IRsvpRepository rsvpRepository)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(profile.IdentityUserId);
        var playerProfileRepository = new Mock<IPlayerProfileRepository>();
        playerProfileRepository
            .Setup(x => x.FindByIdentityUserIdAsync(profile.IdentityUserId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(nowUtc);

        return new SelfCheckInCommandHandler(
            currentUser.Object,
            clock.Object,
            playerProfileRepository.Object,
            sessionRepository.Object,
            eligibilityService,
            rsvpRepository);
    }

    private static IPlayerSessionEligibilityService EligibleService()
    {
        var service = new Mock<IPlayerSessionEligibilityService>();
        service.Setup(x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerSessionEligibilityResult(true, null));
        return service.Object;
    }

    private static Session FutureSession() => new()
    {
        Id = Guid.NewGuid(),
        SeasonId = Guid.NewGuid(),
        VenueId = Guid.NewGuid(),
        Title = "Tuesday Pickup",
        Format = "7v7",
        Capacity = 1,
        TeamCount = 2,
        StartsAtUtc = Utc(2026, 7, 7, 20, 0),
        CheckInOpensAtUtc = Utc(2026, 7, 7, 19, 30),
        CheckInClosesAtUtc = Utc(2026, 7, 7, 19, 45),
        RsvpDeadlineUtc = Utc(2026, 7, 7, 18, 0),
        Status = SessionStatus.Published,
    };

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
