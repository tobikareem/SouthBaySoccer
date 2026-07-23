using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Identity;
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
        context.AddedSession.CheckInOpensAtUtc.Should().Be(GameStartUtc.AddMinutes(-10));
        context.AddedSession.CheckInClosesAtUtc.Should().Be(GameStartUtc.AddMinutes(5));
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
    public async Task HandleAsync_WhenActiveGameHasStarted_RemainsPublishedForGameDay()
    {
        var context = new TestContext();
        // Pickup Pal returned this game from its active feed. Game Day may first be opened at or
        // shortly after kickoff, so the imported session must remain available.
        var pastGame = SampleGame() with { StartsAtUtc = new DateTime(2026, 7, 22, 7, 0, 0, DateTimeKind.Utc) };
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([pastGame]);

        var result = await context.CreateHandler().HandleAsync();

        result.ImportedCount.Should().Be(1);
        result.Warnings.Should().BeEmpty();
        context.AddedSession.Should().NotBeNull();
        context.AddedSession!.Status.Should().Be(SessionStatus.Published);
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

    [Fact]
    public async Task HandleAsync_WhenParticipantsCarryIdentity_CreatesProfilesAndLinksParticipants()
    {
        var context = new TestContext();
        var game = SampleGame() with
        {
            Participants =
            [
                new PickupPalGameParticipantInfo(
                    "p-1", "Mark A", false, false, GameStartUtc.AddDays(-2),
                    UserId: "pp-user-1", PhoneNumberHash: "hash-mark", MaskedPhoneNumber: "+******1111"),
                new PickupPalGameParticipantInfo(
                    "p-2", "tope", true, true, GameStartUtc.AddDays(-1),
                    WhatsAppJidHash: "jid-hash-tope"),
            ],
        };
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([game]);

        await context.CreateHandler().HandleAsync();

        context.AddedProfiles.Should().HaveCount(2);
        var mark = context.AddedProfiles[0];
        mark.DisplayName.Should().Be("Mark A");
        mark.PickupPalUserId.Should().Be("pp-user-1");
        mark.PhoneNumberHash.Should().Be("hash-mark");
        mark.MaskedPhoneNumber.Should().Be("+******1111");
        mark.Role.Should().Be(PlayerRole.Player);
        mark.IsGuest.Should().BeFalse();
        var tope = context.AddedProfiles[1];
        tope.WhatsAppJidHash.Should().Be("jid-hash-tope");
        tope.Role.Should().Be(PlayerRole.Guest);
        tope.IsGuest.Should().BeTrue();
        context.ReplacedParticipants.Should().HaveCount(2);
        context.ReplacedParticipants![0].PlayerProfileId.Should().Be(mark.Id);
        context.ReplacedParticipants[1].PlayerProfileId.Should().Be(tope.Id);
    }

    [Fact]
    public async Task HandleAsync_WhenParticipantHasNoIdentity_LeavesParticipantUnlinked()
    {
        var context = new TestContext();
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleGame()]);

        await context.CreateHandler().HandleAsync();

        context.AddedProfiles.Should().BeEmpty("a participant with no stable identity cannot be deduplicated");
        context.ReplacedParticipants.Should().OnlyContain(participant => participant.PlayerProfileId == null);
    }

    [Fact]
    public async Task HandleAsync_WhenProfileMatchesByPhoneHash_BackfillsKeysWithoutCreating()
    {
        var existing = new PlayerProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = "Old Name",
            NormalizedDisplayName = "OLD NAME",
            PhoneNumberHash = "hash-mark",
            MaskedPhoneNumber = "+******1111",
        };
        var context = new TestContext();
        context.PlayerProfileRepository
            .Setup(x => x.FindByPhoneNumberHashAsync("hash-mark", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var game = SampleGame() with
        {
            Participants =
            [
                new PickupPalGameParticipantInfo(
                    "p-1", "Mark A", false, false, GameStartUtc.AddDays(-2),
                    UserId: "pp-user-1", PhoneNumberHash: "hash-mark", WhatsAppJidHash: "jid-hash-mark"),
            ],
        };
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([game]);

        await context.CreateHandler().HandleAsync();

        context.AddedProfiles.Should().BeEmpty();
        existing.PickupPalUserId.Should().Be("pp-user-1");
        existing.WhatsAppJidHash.Should().Be("jid-hash-mark");
        existing.DisplayName.Should().Be("Mark A", "an import-owned profile follows the Pickup Pal name");
        context.PlayerProfileRepository.Verify(x => x.Update(existing), Times.Once);
        context.ReplacedParticipants![0].PlayerProfileId.Should().Be(existing.Id);
    }

    [Fact]
    public async Task HandleAsync_WhenProfileIsIdentityLinked_KeepsItsDisplayName()
    {
        var claimed = new PlayerProfile
        {
            Id = Guid.NewGuid(),
            IdentityUserId = Guid.NewGuid(),
            DisplayName = "Chosen Name",
            NormalizedDisplayName = "CHOSEN NAME",
            PickupPalUserId = "pp-user-1",
        };
        var context = new TestContext();
        context.PlayerProfileRepository
            .Setup(x => x.FindByPickupPalUserIdAsync("pp-user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimed);
        var game = SampleGame() with
        {
            Participants =
            [
                new PickupPalGameParticipantInfo(
                    "p-1", "Mark A", false, false, GameStartUtc.AddDays(-2), UserId: "pp-user-1"),
            ],
        };
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([game]);

        await context.CreateHandler().HandleAsync();

        claimed.DisplayName.Should().Be("Chosen Name", "sign-in sync owns the name of a claimed profile");
        context.ReplacedParticipants![0].PlayerProfileId.Should().Be(claimed.Id);
    }

    [Fact]
    public async Task HandleAsync_WhenSamePersonIsOnTwoGames_CreatesOneProfile()
    {
        var context = new TestContext();
        var participant = new PickupPalGameParticipantInfo(
            "p-1", "Mark A", false, false, GameStartUtc.AddDays(-2), PhoneNumberHash: "hash-mark");
        var gameOne = SampleGame() with { Participants = [participant] };
        var gameTwo = SampleGame() with
        {
            Id = "game-2",
            StartsAtUtc = GameStartUtc.AddDays(1),
            Participants = [participant with { Id = "p-9" }],
        };
        context.GamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([gameOne, gameTwo]);

        await context.CreateHandler().HandleAsync();

        context.AddedProfiles.Should().HaveCount(1, "profiles pending save must be reused within one pass");
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

        public Mock<IPlayerProfileRepository> PlayerProfileRepository { get; } = new();

        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public List<PlayerProfile> AddedProfiles { get; } = [];

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
            PlayerProfileRepository
                .Setup(x => x.AddAsync(It.IsAny<PlayerProfile>(), It.IsAny<CancellationToken>()))
                .Callback<PlayerProfile, CancellationToken>((profile, _) => AddedProfiles.Add(profile))
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
                PlayerProfileRepository.Object,
                UnitOfWork.Object,
                _clock.Object);
    }
}
