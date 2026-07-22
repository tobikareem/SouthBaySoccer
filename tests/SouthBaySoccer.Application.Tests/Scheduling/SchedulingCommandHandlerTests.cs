using FluentAssertions;
using FluentValidation;
using Moq;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class SchedulingCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenSessionHasInvalidCapacity_ThrowsValidationException()
    {
        var handler = CreateSessionHandler();
        var command = ValidCreateSessionCommand() with { Capacity = 0 };

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task HandleAsync_WhenSessionDeadlineIsAfterStart_ThrowsValidationException()
    {
        var handler = CreateSessionHandler();
        var command = ValidCreateSessionCommand() with { RsvpDeadlineUtc = Utc(2026, 7, 7, 20, 1) };

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task HandleAsync_WhenDuplicateSessionExists_ThrowsConflictWithoutSaving()
    {
        var command = ValidCreateSessionCommand();
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.ExistsDuplicateAsync(
                command.VenueId,
                command.Title,
                command.StartsAtUtc,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var unitOfWork = CreateSavingUnitOfWork();
        var handler = CreateSessionHandler(sessionRepository, unitOfWork);

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<ApplicationConflictException>();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenNoDuplicateExists_CreatesSession()
    {
        var command = ValidCreateSessionCommand();
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.ExistsDuplicateAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var unitOfWork = CreateSavingUnitOfWork();
        var handler = CreateSessionHandler(sessionRepository, unitOfWork);

        var result = await handler.HandleAsync(command);

        result.Title.Should().Be(command.Title);
        sessionRepository.Verify(
            x => x.AddAsync(It.IsAny<Session>(), It.IsAny<CancellationToken>()),
            Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenOccurrenceAlreadyExists_ReturnsExistingSessionWithoutSaving()
    {
        var recurrenceRuleId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var venueId = Guid.NewGuid();
        var occurrenceStart = Utc(2026, 7, 7, 20, 0);
        var expectedOccurrenceKey = $"{recurrenceRuleId:N}:20260707T200000Z";
        var existingSession = new Session
        {
            Id = Guid.NewGuid(),
            SeasonId = seasonId,
            VenueId = venueId,
            RecurrenceRuleId = recurrenceRuleId,
            Title = "Tuesday Pickup",
            Format = "7v7",
            Capacity = 22,
            TeamCount = 2,
            StartsAtUtc = occurrenceStart,
            CheckInOpensAtUtc = Utc(2026, 7, 7, 19, 30),
            CheckInClosesAtUtc = Utc(2026, 7, 7, 19, 45),
            RsvpDeadlineUtc = Utc(2026, 7, 7, 18, 0),
            OccurrenceKey = expectedOccurrenceKey,
            Status = SessionStatus.Published,
        };
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.FindRecurrenceRuleAsync(recurrenceRuleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecurrenceRule { Id = recurrenceRuleId, Name = "Weekly", TimeZoneId = "America/Los_Angeles", Rule = "FREQ=WEEKLY" });
        sessionRepository
            .Setup(x => x.FindByOccurrenceKeyAsync(expectedOccurrenceKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSession);
        var unitOfWork = new Mock<IUnitOfWork>();
        var handler = new CreateSessionOccurrenceCommandHandler(
            new CreateSessionOccurrenceCommandValidator(),
            CreateSessionHandler(sessionRepository: sessionRepository, unitOfWork: unitOfWork),
            sessionRepository.Object);

        var result = await handler.HandleAsync(new CreateSessionOccurrenceCommand(
            recurrenceRuleId,
            seasonId,
            venueId,
            occurrenceStart,
            "Tuesday Pickup",
            "7v7",
            22,
            2,
            Utc(2026, 7, 7, 19, 30),
            Utc(2026, 7, 7, 19, 45),
            Utc(2026, 7, 7, 18, 0)));

        result.SessionId.Should().Be(existingSession.Id);
        result.OccurrenceKey.Should().Be(expectedOccurrenceKey);
        sessionRepository.Verify(x => x.AddAsync(It.IsAny<Session>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenDeletingExistingSession_SoftDeletesAndSaves()
    {
        var session = new Session { Id = Guid.NewGuid(), Title = "Saturday pickup" };
        var sessionRepository = new Mock<ISessionRepository>();
        sessionRepository
            .Setup(x => x.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var unitOfWork = CreateSavingUnitOfWork();
        var handler = new DeleteSessionCommandHandler(sessionRepository.Object, unitOfWork.Object);

        await handler.HandleAsync(new DeleteSessionCommand(session.Id));

        sessionRepository.Verify(x => x.SoftDelete(session), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenDeletingMissingSession_ThrowsNotFoundWithoutSaving()
    {
        var sessionRepository = new Mock<ISessionRepository>();
        var unitOfWork = CreateSavingUnitOfWork();
        var handler = new DeleteSessionCommandHandler(sessionRepository.Object, unitOfWork.Object);

        var act = () => handler.HandleAsync(new DeleteSessionCommand(Guid.NewGuid()));

        await act.Should().ThrowAsync<ApplicationNotFoundException>();
        sessionRepository.Verify(x => x.SoftDelete(It.IsAny<Session>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CreateSessionCommandHandler CreateSessionHandler(
        Mock<ISessionRepository>? sessionRepository = null,
        Mock<IUnitOfWork>? unitOfWork = null)
    {
        var seasonId = ValidCreateSessionCommand().SeasonId;
        var venueId = ValidCreateSessionCommand().VenueId;
        var seasonRepository = new Mock<ISeasonRepository>();
        seasonRepository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new Season { Id = id, Name = "2026" });
        var venueRepository = new Mock<IVenueRepository>();
        venueRepository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new Venue { Id = id, Name = "Wilson Park", Locality = "Torrance" });

        _ = seasonId;
        _ = venueId;

        return new CreateSessionCommandHandler(
            new CreateSessionCommandValidator(),
            seasonRepository.Object,
            venueRepository.Object,
            (sessionRepository ?? new Mock<ISessionRepository>()).Object,
            (unitOfWork ?? CreateSavingUnitOfWork()).Object);
    }

    private static Mock<IUnitOfWork> CreateSavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static CreateSessionCommand ValidCreateSessionCommand() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Tuesday Pickup",
            "7v7",
            22,
            2,
            Utc(2026, 7, 7, 20, 0),
            Utc(2026, 7, 7, 19, 30),
            Utc(2026, 7, 7, 19, 45),
            Utc(2026, 7, 7, 18, 0));

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
