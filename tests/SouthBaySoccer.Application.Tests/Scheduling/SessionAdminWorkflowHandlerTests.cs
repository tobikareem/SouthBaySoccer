using FluentAssertions;
using FluentValidation;
using Moq;
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

