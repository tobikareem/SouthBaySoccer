using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class SessionAdminWorkflowHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenCreatingDraft_AddsDraftSession()
    {
        Session? added = null;
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.AddAsync(It.IsAny<Session>(), It.IsAny<CancellationToken>()))
            .Callback<Session, CancellationToken>((session, _) => added = session)
            .Returns(Task.CompletedTask);
        var handler = new CreateSessionDraftCommandHandler(
            new CreateSessionCommandValidator(),
            SeasonRepository(),
            VenueRepository(),
            sessionRepository.Object,
            SavingUnitOfWork().Object);

        var result = await handler.HandleAsync(ValidDraftCommand());

        result.Status.Should().Be(nameof(SessionStatus.Draft));
        added.Should().NotBeNull();
        added!.Status.Should().Be(SessionStatus.Draft);
        added.Title.Should().Be("Marina Field - Saturday pickup");
    }

    [Fact]
    public async Task HandleAsync_WhenDuplicateDraftSessionExists_ThrowsConflictWithoutAdding()
    {
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.ExistsDuplicateAsync(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "Marina Field - Saturday pickup",
                Utc(2026, 7, 11, 19, 40),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var handler = new CreateSessionDraftCommandHandler(
            new CreateSessionCommandValidator(),
            SeasonRepository(),
            VenueRepository(),
            sessionRepository.Object,
            SavingUnitOfWork().Object);

        var act = () => handler.HandleAsync(ValidDraftCommand());

        await act.Should().ThrowAsync<ApplicationConflictException>();
        sessionRepository.Verify(
            x => x.AddAsync(It.IsAny<Session>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenCreatingDraft_TitleUsesSessionsActualLocalWeekday()
    {
        var sessionRepository = new Mock<ISessionRepository>();
        var handler = new CreateSessionDraftCommandHandler(
            new CreateSessionCommandValidator(),
            SeasonRepository(),
            VenueRepository(),
            sessionRepository.Object,
            SavingUnitOfWork().Object);
        // 2026-07-08 19:40 UTC is 12:40 PM Pacific — a Wednesday, not the hardcoded "Saturday".
        var command = ValidDraftCommand() with
        {
            StartsAtUtc = Utc(2026, 7, 8, 19, 40),
            CheckInOpensAtUtc = Utc(2026, 7, 8, 19, 30),
            CheckInClosesAtUtc = Utc(2026, 7, 8, 19, 40),
            RsvpDeadlineUtc = Utc(2026, 7, 8, 18, 30),
        };

        var result = await handler.HandleAsync(command);

        result.Title.Should().Be("Marina Field - Wednesday pickup");
    }

    [Fact]
    public async Task HandleAsync_WhenNoSeasonCoversStartDate_ThrowsConflict()
    {
        var seasonRepository = new Mock<ISeasonRepository>();
        seasonRepository
            .Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Season
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "Spring 2026",
                    StartsAtUtc = Utc(2026, 1, 1, 0, 0),
                    EndsAtUtc = Utc(2026, 3, 31, 23, 59),
                },
            ]);
        var handler = new CreateSessionDraftCommandHandler(
            new CreateSessionCommandValidator(),
            seasonRepository.Object,
            VenueRepository(),
            new Mock<ISessionRepository>().Object,
            SavingUnitOfWork().Object);
        // Session start (July) falls outside the only active season (Jan-Mar) — there is no season to
        // silently fall back to.
        var command = ValidDraftCommand();

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("No season covers the session start date.");
    }

    [Fact]
    public async Task HandleAsync_WhenSessionForAdminEditIsMissing_ThrowsNotFound()
    {
        var sessionId = Guid.NewGuid();
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);
        var handler = new GetSessionForAdminEditQueryHandler(sessionRepository.Object, VenueRepository());

        var act = async () => await handler.HandleAsync(sessionId);

        await act.Should().ThrowAsync<ApplicationNotFoundException>()
            .WithMessage("Session was not found.");
    }

    [Fact]
    public async Task HandleAsync_WhenPublishingAlreadyPublishedSession_ReturnsWithoutSaving()
    {
        var sessionId = Guid.NewGuid();
        var session = ValidSession(sessionId);
        session.Status = SessionStatus.Published;
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var unitOfWork = SavingUnitOfWork();
        var handler = new PublishSessionCommandHandler(sessionRepository.Object, unitOfWork.Object);

        var result = await handler.HandleAsync(sessionId);

        result.SessionId.Should().Be(sessionId);
        result.Status.Should().Be(nameof(SessionStatus.Published));
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenPublishingDraftSession_TransitionsToPublishedAndSaves()
    {
        var sessionId = Guid.NewGuid();
        var session = ValidSession(sessionId);
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var unitOfWork = SavingUnitOfWork();
        var handler = new PublishSessionCommandHandler(sessionRepository.Object, unitOfWork.Object);

        var result = await handler.HandleAsync(sessionId);

        result.Status.Should().Be(nameof(SessionStatus.Published));
        session.Status.Should().Be(SessionStatus.Published);
        sessionRepository.Verify(x => x.Update(session), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenPublishingCanceledSession_ThrowsConflict()
    {
        var sessionId = Guid.NewGuid();
        var session = ValidSession(sessionId);
        session.Status = SessionStatus.Canceled;
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var handler = new PublishSessionCommandHandler(sessionRepository.Object, SavingUnitOfWork().Object);

        var act = () => handler.HandleAsync(sessionId);

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("Only draft sessions can be published.");
    }

    [Fact]
    public async Task HandleAsync_WhenPublishingCompletedSession_ThrowsConflict()
    {
        var sessionId = Guid.NewGuid();
        var session = ValidSession(sessionId);
        session.Status = SessionStatus.Completed;
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var handler = new PublishSessionCommandHandler(sessionRepository.Object, SavingUnitOfWork().Object);

        var act = () => handler.HandleAsync(sessionId);

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("Only draft sessions can be published.");
    }

    [Fact]
    public async Task HandleAsync_WhenUpdatingDraftSession_AppliesChangesAndSaves()
    {
        var sessionId = Guid.NewGuid();
        var session = ValidSession(sessionId);
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var unitOfWork = SavingUnitOfWork();
        var handler = new UpdateSessionAdminCommandHandler(
            new CreateSessionCommandValidator(),
            SeasonRepository(),
            VenueRepository(),
            sessionRepository.Object,
            unitOfWork.Object);

        var result = await handler.HandleAsync(ValidUpdateCommand(sessionId) with { Capacity = 24 });

        result.SessionId.Should().Be(sessionId);
        result.Capacity.Should().Be(24);
        session.Capacity.Should().Be(24);
        sessionRepository.Verify(x => x.Update(session), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenUpdatingCanceledSession_ThrowsConflict()
    {
        var sessionId = Guid.NewGuid();
        var session = ValidSession(sessionId);
        session.Status = SessionStatus.Canceled;
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var handler = new UpdateSessionAdminCommandHandler(
            new CreateSessionCommandValidator(),
            SeasonRepository(),
            VenueRepository(),
            sessionRepository.Object,
            SavingUnitOfWork().Object);

        var act = () => handler.HandleAsync(ValidUpdateCommand(sessionId));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("Canceled or completed sessions cannot be updated.");
    }

    [Fact]
    public async Task HandleAsync_WhenUpdatingCompletedSession_ThrowsConflict()
    {
        var sessionId = Guid.NewGuid();
        var session = ValidSession(sessionId);
        session.Status = SessionStatus.Completed;
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var handler = new UpdateSessionAdminCommandHandler(
            new CreateSessionCommandValidator(),
            SeasonRepository(),
            VenueRepository(),
            sessionRepository.Object,
            SavingUnitOfWork().Object);

        var act = () => handler.HandleAsync(ValidUpdateCommand(sessionId));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("Canceled or completed sessions cannot be updated.");
    }

    [Fact]
    public async Task HandleAsync_WhenSessionForUpdateIsMissing_ThrowsNotFound()
    {
        var sessionId = Guid.NewGuid();
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);
        var handler = new UpdateSessionAdminCommandHandler(
            new CreateSessionCommandValidator(),
            SeasonRepository(),
            VenueRepository(),
            sessionRepository.Object,
            SavingUnitOfWork().Object);

        var act = () => handler.HandleAsync(ValidUpdateCommand(sessionId));

        await act.Should().ThrowAsync<ApplicationNotFoundException>()
            .WithMessage("Session was not found.");
    }

    [Fact]
    public async Task HandleAsync_WhenSessionForAdminEditExists_ReturnsMappedEditModel()
    {
        var sessionId = Guid.NewGuid();
        var session = ValidSession(sessionId);
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var handler = new GetSessionForAdminEditQueryHandler(sessionRepository.Object, VenueRepository());

        var result = await handler.HandleAsync(sessionId);

        result.SessionId.Should().Be(sessionId);
        result.VenueId.Should().Be(session.VenueId);
        result.VenueName.Should().Be("Marina Field");
        result.Format.Should().Be(session.Format);
        result.Capacity.Should().Be(session.Capacity);
        result.TeamCount.Should().Be(session.TeamCount);
        result.StartsAtUtc.Should().Be(session.StartsAtUtc);
        result.CheckInOpensAtUtc.Should().Be(session.CheckInOpensAtUtc);
        result.CheckInClosesAtUtc.Should().Be(session.CheckInClosesAtUtc);
        result.RsvpDeadlineUtc.Should().Be(session.RsvpDeadlineUtc);
        result.Status.Should().Be(nameof(SessionStatus.Draft));
    }

    [Fact]
    public async Task HandleAsync_WhenTakeExceedsMaximum_ClampsRequestTo100()
    {
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.ListManagedAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Session>());
        var venueRepository = new Mock<IVenueRepository>();
        venueRepository
            .Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Venue>());
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Utc(2026, 7, 10, 12, 0));
        var handler = new ListManagedSessionsQueryHandler(clock.Object, sessionRepository.Object, venueRepository.Object);

        await handler.HandleAsync(take: 500);

        sessionRepository.Verify(
            x => x.ListManagedAsync(It.IsAny<DateTime>(), 100, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenTakeIsBelowMinimum_ClampsRequestTo1()
    {
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.ListManagedAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Session>());
        var venueRepository = new Mock<IVenueRepository>();
        venueRepository
            .Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Venue>());
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Utc(2026, 7, 10, 12, 0));
        var handler = new ListManagedSessionsQueryHandler(clock.Object, sessionRepository.Object, venueRepository.Object);

        await handler.HandleAsync(take: -5);

        sessionRepository.Verify(
            x => x.ListManagedAsync(It.IsAny<DateTime>(), 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenSessionVenueIsNotAmongActiveVenues_ReturnsUnknownVenueLabel()
    {
        var session = ValidSession(Guid.NewGuid());
        session.VenueId = Guid.NewGuid();
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.ListManagedAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        var venueRepository = new Mock<IVenueRepository>();
        venueRepository
            .Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Venue>());
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Utc(2026, 7, 10, 12, 0));
        var handler = new ListManagedSessionsQueryHandler(clock.Object, sessionRepository.Object, venueRepository.Object);

        var result = await handler.HandleAsync();

        result.Should().ContainSingle();
        result[0].VenueName.Should().Be("Unknown venue");
    }

    [Fact]
    public async Task HandleAsync_WhenTodayIsSaturday_ReturnsNextWeeksSaturday()
    {
        var venueRepository = EmptyVenueRepository();
        var clock = new Mock<IClock>();
        // 2026-07-11 19:40 UTC is 12:40 PM Pacific — a Saturday.
        clock.SetupGet(x => x.UtcNow).Returns(Utc(2026, 7, 11, 19, 40));
        var handler = new GetCreateSessionAdminDefaultsQueryHandler(
            clock.Object,
            venueRepository,
            NoOpImportHandler(),
            NullLogger<GetCreateSessionAdminDefaultsQueryHandler>.Instance);

        var result = await handler.HandleAsync();

        result.DefaultGameDateLocal.Should().Be(new DateTime(2026, 7, 18));
    }

    [Fact]
    public async Task HandleAsync_WhenTodayIsFriday_ReturnsTomorrowsSaturday()
    {
        var venueRepository = EmptyVenueRepository();
        var clock = new Mock<IClock>();
        // 2026-07-10 19:40 UTC is 12:40 PM Pacific — a Friday, the day before the Saturday fixture above.
        clock.SetupGet(x => x.UtcNow).Returns(Utc(2026, 7, 10, 19, 40));
        var handler = new GetCreateSessionAdminDefaultsQueryHandler(
            clock.Object,
            venueRepository,
            NoOpImportHandler(),
            NullLogger<GetCreateSessionAdminDefaultsQueryHandler>.Instance);

        var result = await handler.HandleAsync();

        result.DefaultGameDateLocal.Should().Be(new DateTime(2026, 7, 11));
    }

    [Fact]
    public async Task HandleAsync_WhenTodayIsSunday_ReturnsSaturdaySixDaysLater()
    {
        var venueRepository = EmptyVenueRepository();
        var clock = new Mock<IClock>();
        // 2026-07-12 19:40 UTC is 12:40 PM Pacific — the Sunday right after the Saturday fixture above.
        clock.SetupGet(x => x.UtcNow).Returns(Utc(2026, 7, 12, 19, 40));
        var handler = new GetCreateSessionAdminDefaultsQueryHandler(
            clock.Object,
            venueRepository,
            NoOpImportHandler(),
            NullLogger<GetCreateSessionAdminDefaultsQueryHandler>.Instance);

        var result = await handler.HandleAsync();

        result.DefaultGameDateLocal.Should().Be(new DateTime(2026, 7, 18));
    }

    private static UpdateSessionAdminCommand ValidUpdateCommand(Guid sessionId) =>
        new(
            sessionId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Marina Field",
            "7v7",
            20,
            2,
            Utc(2026, 7, 11, 19, 40),
            Utc(2026, 7, 11, 19, 30),
            Utc(2026, 7, 11, 19, 40),
            Utc(2026, 7, 11, 18, 30));

    private static IVenueRepository EmptyVenueRepository()
    {
        var repository = new Mock<IVenueRepository>();
        repository
            .Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Venue>());
        return repository.Object;
    }

    // A real import handler whose games client returns no games, so the Pickup Pal import step in
    // the defaults handler is a no-op and the defaults behavior under test stays isolated.
    private static ImportPickupPalGamesCommandHandler NoOpImportHandler()
    {
        var gamesClient = new Mock<IPickupPalGamesClient>();
        gamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PickupPalGame>());
        return new ImportPickupPalGamesCommandHandler(
            gamesClient.Object,
            Mock.Of<IPickupPalGameRepository>(),
            Mock.Of<ISessionRepository>(),
            Mock.Of<ISeasonRepository>(),
            EmptyVenueRepository(),
            SavingUnitOfWork().Object,
            Mock.Of<IClock>());
    }

    private static CreateSessionDraftCommand ValidDraftCommand() =>
        new(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Marina Field",
            "7v7",
            20,
            2,
            Utc(2026, 7, 11, 19, 40),
            Utc(2026, 7, 11, 19, 30),
            Utc(2026, 7, 11, 19, 40),
            Utc(2026, 7, 11, 18, 30));

    private static Session ValidSession(Guid id) =>
        new()
        {
            Id = id,
            SeasonId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            VenueId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Title = "Marina Field - Saturday pickup",
            Format = "7v7",
            Capacity = 20,
            TeamCount = 2,
            StartsAtUtc = Utc(2026, 7, 11, 19, 40),
            CheckInOpensAtUtc = Utc(2026, 7, 11, 19, 30),
            CheckInClosesAtUtc = Utc(2026, 7, 11, 19, 40),
            RsvpDeadlineUtc = Utc(2026, 7, 11, 18, 30),
            Status = SessionStatus.Draft,
        };

    private static ISeasonRepository SeasonRepository()
    {
        var repository = new Mock<ISeasonRepository>();
        repository
            .Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Season
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "2026",
                    StartsAtUtc = Utc(2026, 1, 1, 0, 0),
                    EndsAtUtc = Utc(2026, 12, 31, 23, 59),
                },
            ]);
        return repository.Object;
    }

    private static IVenueRepository VenueRepository()
    {
        var repository = new Mock<IVenueRepository>();
        repository
            .Setup(x => x.GetByIdAsync(Guid.Parse("22222222-2222-2222-2222-222222222222"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Venue
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Marina Field",
                Locality = "Redondo Beach",
            });
        return repository.Object;
    }

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}

