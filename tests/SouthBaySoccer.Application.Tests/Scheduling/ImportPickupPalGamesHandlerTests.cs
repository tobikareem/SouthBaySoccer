using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class ImportPickupPalGamesHandlerTests
{
    private static readonly DateTime GameStartUtc = new(2026, 7, 24, 4, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_WhenGameIsNew_CreatesPublishedSessionSnapshotAndParticipants()
    {
        var context = new TestContext();
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleGame()]);

        var result = await context.CreateHandler().HandleAsync();

        result.ImportedCount.Should().Be(1);
        result.Warnings.Should().BeEmpty();
        context.AddedSession.Should().NotBeNull();
        context.AddedSession!.Status.Should().Be(SessionStatus.Published);
        context.AddedSession.Capacity.Should().Be(10);
        context.AddedSession.StartsAtUtc.Should().Be(GameStartUtc);
        context.AddedSession.RsvpDeadlineUtc.Should().Be(GameStartUtc.AddHours(-1));
        context.AddedSession.OccurrenceKey.Should().Be("pickuppal:game-1");
        context.AddedSnapshot.Should().NotBeNull();
        context.AddedSnapshot!.PickupPalGameId.Should().Be("game-1");
        context.AddedSnapshot.SanitizedGameJson.Should().Contain("Mark A").And.NotContain("whatsapp");
        context.ReplacedParticipants.Should().HaveCount(2);
        context.ReplacedParticipants![0].DisplayName.Should().Be("Mark A");
        context.ReplacedParticipants[1].IsWaitlist.Should().BeTrue();
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenSessionExistsWithMatchingOccurrenceKey_AdoptsAndOverwritesIt()
    {
        var existing = new Session
        {
            Id = Guid.NewGuid(),
            Capacity = 20,
            Title = "Previously imported",
            StartsAtUtc = GameStartUtc,
            OccurrenceKey = "pickuppal:game-1",
            Status = SessionStatus.Published,
        };
        var context = new TestContext();
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleGame()]);
        context.SessionRepository
            .Setup(x => x.FindByOccurrenceKeyAsync("pickuppal:game-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await context.CreateHandler().HandleAsync();

        result.ImportedCount.Should().Be(1);
        context.AddedSession.Should().BeNull("a session this import previously created is adopted, not duplicated");
        existing.Capacity.Should().Be(10, "Pickup Pal is the source of truth on re-import");
        context.SessionRepository.Verify(x => x.Update(existing), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenOnlyAStartTimeCoincides_CreatesNewSessionInsteadOfAdopting()
    {
        // No snapshot and no occurrence-key match: a manual session that merely shares the start
        // time must never be adopted. The import creates its own session instead.
        var context = new TestContext();
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleGame()]);

        var result = await context.CreateHandler().HandleAsync();

        result.ImportedCount.Should().Be(1);
        context.AddedSession.Should().NotBeNull("a coincidental same-time session must not be overwritten");
        context.AddedSession!.OccurrenceKey.Should().Be("pickuppal:game-1");
    }

    [Fact]
    public async Task HandleAsync_WhenGameStartIsInThePast_ImportsAsDraftWithWarning()
    {
        var context = new TestContext();
        // Clock is 2026-07-22 08:00 UTC; a start an hour earlier is in the past but still covered by
        // the active season, so the game imports but must not auto-publish.
        var pastGame = SampleGame() with { StartsAtUtc = new DateTime(2026, 7, 22, 7, 0, 0, DateTimeKind.Utc) };
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([pastGame]);

        var result = await context.CreateHandler().HandleAsync();

        result.ImportedCount.Should().Be(1);
        result.Warnings.Should().ContainSingle(warning => warning.Contains("draft") && warning.Contains("past"));
        context.AddedSession.Should().NotBeNull();
        context.AddedSession!.Status.Should().Be(SessionStatus.Draft);
    }

    [Fact]
    public async Task HandleAsync_WhenGameHasNoCapacity_ImportsAsDraftWithWarning()
    {
        var context = new TestContext();
        var noCapacityGame = SampleGame() with { MaxPlayers = 0 };
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([noCapacityGame]);

        var result = await context.CreateHandler().HandleAsync();

        result.ImportedCount.Should().Be(1);
        result.Warnings.Should().ContainSingle(warning => warning.Contains("draft") && warning.Contains("capacity"));
        context.AddedSession.Should().NotBeNull();
        context.AddedSession!.Status.Should().Be(SessionStatus.Draft);
        context.AddedSession.Capacity.Should().Be(1, "capacity is clamped to a positive value for the check constraint");
    }

    [Fact]
    public async Task HandleAsync_WhenSnapshotExists_ReusesItsSessionAndUpdatesSnapshot()
    {
        var session = new Session { Id = Guid.NewGuid(), StartsAtUtc = GameStartUtc };
        var snapshot = new PickupPalGameSnapshot
        {
            Id = Guid.NewGuid(),
            PickupPalGameId = "game-1",
            SessionId = session.Id,
        };
        var context = new TestContext();
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleGame()]);
        context.GameRepository
            .Setup(x => x.FindSnapshotByGameIdAsync("game-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        context.SessionRepository
            .Setup(x => x.GetByIdAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        await context.CreateHandler().HandleAsync();

        context.AddedSession.Should().BeNull();
        context.AddedSnapshot.Should().BeNull();
        context.GameRepository.Verify(x => x.UpdateSnapshot(snapshot), Times.Once);
        snapshot.MaxPlayers.Should().Be(10);
    }

    [Fact]
    public async Task HandleAsync_WhenNoSeasonCoversGame_SkipsWithWarningAndDoesNotSave()
    {
        var context = new TestContext(seasonCoversGame: false);
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleGame()]);

        var result = await context.CreateHandler().HandleAsync();

        result.ImportedCount.Should().Be(0);
        result.SkippedCount.Should().Be(1);
        result.Warnings.Should().ContainSingle(warning => warning.Contains("game-1"));
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenVenueMissing_AutoCreatesVenueFromLocation()
    {
        var context = new TestContext();
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleGame()]);

        await context.CreateHandler().HandleAsync();

        context.AddedVenue.Should().NotBeNull();
        context.AddedVenue!.Name.Should().Be("969 e caribbean dr, sunnyvale, ca 94089");
        context.AddedVenue.Locality.Should().Be("Imported from Pickup Pal");
    }

    private static PickupPalGame SampleGame() =>
        new(
            "game-1",
            GameStartUtc,
            "969 e caribbean dr, sunnyvale, ca 94089",
            10,
            "active",
            "Fire FC",
            [
                new PickupPalGameParticipantInfo("p-1", "Mark A", false, false, GameStartUtc.AddDays(-2)),
                new PickupPalGameParticipantInfo("p-2", "tope", true, true, GameStartUtc.AddDays(-1)),
            ]);

    private sealed class TestContext
    {
        public Mock<IPickupPalGamesClient> GamesClient { get; } = new();

        public Mock<IPickupPalGameRepository> GameRepository { get; } = new();

        public Mock<ISessionRepository> SessionRepository { get; } = new();

        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public Session? AddedSession { get; private set; }

        public PickupPalGameSnapshot? AddedSnapshot { get; private set; }

        public Venue? AddedVenue { get; private set; }

        public IReadOnlyList<PickupPalGameParticipant>? ReplacedParticipants { get; private set; }

        private readonly Mock<ISeasonRepository> _seasonRepository = new();
        private readonly Mock<IVenueRepository> _venueRepository = new();
        private readonly Mock<IClock> _clock = new();

        public TestContext(bool seasonCoversGame = true)
        {
            _clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 7, 22, 8, 0, 0, DateTimeKind.Utc));
            _seasonRepository
                .Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(seasonCoversGame
                    ? [new Season { Id = Guid.NewGuid(), Name = "Summer 2026", StartsAtUtc = GameStartUtc.AddMonths(-1), EndsAtUtc = GameStartUtc.AddMonths(2) }]
                    : Array.Empty<Season>());
            _venueRepository
                .Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<Venue>());
            _venueRepository
                .Setup(x => x.AddAsync(It.IsAny<Venue>(), It.IsAny<CancellationToken>()))
                .Callback<Venue, CancellationToken>((venue, _) => AddedVenue = venue)
                .Returns(Task.CompletedTask);
            SessionRepository
                .Setup(x => x.AddAsync(It.IsAny<Session>(), It.IsAny<CancellationToken>()))
                .Callback<Session, CancellationToken>((session, _) => AddedSession = session)
                .Returns(Task.CompletedTask);
            GameRepository
                .Setup(x => x.AddSnapshotAsync(It.IsAny<PickupPalGameSnapshot>(), It.IsAny<CancellationToken>()))
                .Callback<PickupPalGameSnapshot, CancellationToken>((snapshot, _) => AddedSnapshot = snapshot)
                .Returns(Task.CompletedTask);
            GameRepository
                .Setup(x => x.ReplaceParticipantsAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<IReadOnlyList<PickupPalGameParticipant>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Guid, IReadOnlyList<PickupPalGameParticipant>, CancellationToken>(
                    (_, participants, _) => ReplacedParticipants = participants)
                .Returns(Task.CompletedTask);
            UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public ImportPickupPalGamesCommandHandler CreateHandler() =>
            new(
                GamesClient.Object,
                GameRepository.Object,
                SessionRepository.Object,
                _seasonRepository.Object,
                _venueRepository.Object,
                UnitOfWork.Object,
                _clock.Object);
    }
}
