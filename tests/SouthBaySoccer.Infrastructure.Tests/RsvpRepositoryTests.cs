using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure;
using SouthBaySoccer.Infrastructure.Persistence;
using Xunit;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class RsvpRepositoryTests
{
    private readonly InfrastructureDatabaseFixture database;

    public RsvpRepositoryTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task SubmitRsvpAsync_WhenCapacityIsAvailable_CreatesGoingRsvp()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (session, player, _) = await SeedSessionAsync(db, capacity: 1);
        var repository = scope.ServiceProvider.GetRequiredService<IRsvpRepository>();

        var result = await repository.SubmitRsvpAsync(session.Id, player.Id, RsvpStatus.Going);

        result.State.Should().Be(RsvpMutationState.Going);
        (await db.RsvpResponses.CountAsync(x => x.SessionId == session.Id && x.Status == RsvpStatus.Going)).Should().Be(1);
        (await db.WaitlistEntries.CountAsync(x => x.SessionId == session.Id)).Should().Be(0);
    }

    [Fact]
    public async Task SubmitRsvpAsync_WhenSessionIsFull_CreatesWaitlistWithoutWaitlistedRsvpStatus()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (session, confirmedPlayer, waitlistedPlayer) = await SeedSessionAsync(db, capacity: 1);
        var repository = scope.ServiceProvider.GetRequiredService<IRsvpRepository>();

        await repository.SubmitRsvpAsync(session.Id, confirmedPlayer.Id, RsvpStatus.Going);
        var result = await repository.SubmitRsvpAsync(session.Id, waitlistedPlayer.Id, RsvpStatus.Going);

        result.State.Should().Be(RsvpMutationState.Waitlisted);
        result.WaitlistPosition.Should().Be(1);
        (await db.WaitlistEntries.CountAsync(x => x.SessionId == session.Id && x.PlayerProfileId == waitlistedPlayer.Id && x.Status == WaitlistEntryStatus.Active)).Should().Be(1);
        (await db.RsvpResponses.AnyAsync(x => x.SessionId == session.Id && x.PlayerProfileId == waitlistedPlayer.Id && x.Status == RsvpStatus.Going)).Should().BeFalse();
    }

    [Fact]
    public async Task SubmitRsvpAsync_WhenImportedRosterFillsCapacity_CreatesWaitlist()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (session, importedPlayer, waitlistedPlayer) = await SeedSessionAsync(db, capacity: 1);
        await db.Set<PickupPalGameParticipant>().AddAsync(
            new PickupPalGameParticipant
            {
                SessionId = session.Id,
                PlayerProfileId = importedPlayer.Id,
                PickupPalParticipantId = $"participant-{Guid.NewGuid():N}",
                DisplayName = importedPlayer.DisplayName,
                IsWaitlist = false,
                DisplayOrder = 0,
                JoinedAtUtc = Utc(2026, 7, 7, 15, 0)
            });
        await db.SaveChangesAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IRsvpRepository>();

        var result = await repository.SubmitRsvpAsync(
            session.Id,
            waitlistedPlayer.Id,
            RsvpStatus.Going);

        result.State.Should().Be(RsvpMutationState.Waitlisted);
        (await db.WaitlistEntries.AnyAsync(x =>
            x.SessionId == session.Id
            && x.PlayerProfileId == waitlistedPlayer.Id
            && x.Status == WaitlistEntryStatus.Active)).Should().BeTrue();
        (await db.RsvpResponses.AnyAsync(x =>
            x.SessionId == session.Id
            && x.PlayerProfileId == waitlistedPlayer.Id
            && x.Status == RsvpStatus.Going)).Should().BeFalse();
    }

    [Fact]
    public async Task CancelAndPromoteAsync_WhenConfirmedPlayerCancels_PromotesNextEligibleWaitlistEntryAndWritesOutboxMessage()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (session, confirmedPlayer, waitlistedPlayer) = await SeedSessionAsync(db, capacity: 1);
        var repository = scope.ServiceProvider.GetRequiredService<IRsvpRepository>();
        await repository.SubmitRsvpAsync(session.Id, confirmedPlayer.Id, RsvpStatus.Going);
        await repository.SubmitRsvpAsync(session.Id, waitlistedPlayer.Id, RsvpStatus.Going);

        var result = await repository.CancelAndPromoteAsync(
            session.Id,
            confirmedPlayer.Id,
            (_, _) => Task.FromResult<IReadOnlyDictionary<Guid, bool>>(
                new Dictionary<Guid, bool> { [waitlistedPlayer.Id] = true }));

        result.State.Should().Be(RsvpMutationState.Canceled);
        result.PromotedPlayerProfileId.Should().Be(waitlistedPlayer.Id);
        (await db.RsvpResponses.AnyAsync(x => x.SessionId == session.Id && x.PlayerProfileId == confirmedPlayer.Id)).Should().BeFalse();
        (await db.RsvpResponses.AnyAsync(x => x.SessionId == session.Id && x.PlayerProfileId == waitlistedPlayer.Id && x.Status == RsvpStatus.Going)).Should().BeTrue();
        var promotedWaitlistEntry = await db.WaitlistEntries.SingleAsync(x => x.SessionId == session.Id && x.PlayerProfileId == waitlistedPlayer.Id, CancellationToken.None);
        promotedWaitlistEntry.Status.Should().Be(WaitlistEntryStatus.Promoted);
        var outboxMessages = await db.OutboxMessages.Where(x => x.MessageType == "PlayerWaitlistPromoted").ToArrayAsync();
        var outboxMessage = outboxMessages.Single(x => GetPayloadGuid(x.PayloadJson, "SessionId") == session.Id);
        outboxMessage.Status.Should().Be(OutboxMessageStatus.Pending);
        outboxMessage.AvailableAtUtc.Should().Be(Utc(2026, 7, 7, 16, 0));
        using var payload = JsonDocument.Parse(outboxMessage.PayloadJson);
        payload.RootElement.GetProperty("SessionId").GetGuid().Should().Be(session.Id);
        payload.RootElement.GetProperty("PlayerProfileId").GetGuid().Should().Be(waitlistedPlayer.Id);
        payload.RootElement.GetProperty("WaitlistEntryId").GetGuid().Should().Be(promotedWaitlistEntry.Id);
        payload.RootElement.GetProperty("PromotedAtUtc").GetDateTime().Should().Be(Utc(2026, 7, 7, 16, 0));
    }

    [Fact]
    public async Task CancelAndPromoteAsync_WhenWaitlistedPlayerCancels_DoesNotPromoteOrWriteOutboxMessage()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (session, confirmedPlayer, waitlistedPlayer) = await SeedSessionAsync(db, capacity: 1);
        var secondWaitlistedPlayer = CreatePlayer("Kemi");
        await db.PlayerProfiles.AddAsync(secondWaitlistedPlayer);
        await db.SaveChangesAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IRsvpRepository>();
        await repository.SubmitRsvpAsync(session.Id, confirmedPlayer.Id, RsvpStatus.Going);
        await repository.SubmitRsvpAsync(session.Id, waitlistedPlayer.Id, RsvpStatus.Going);
        await repository.SubmitRsvpAsync(session.Id, secondWaitlistedPlayer.Id, RsvpStatus.Going);

        var result = await repository.CancelAndPromoteAsync(
            session.Id,
            waitlistedPlayer.Id,
            (_, _) => Task.FromResult<IReadOnlyDictionary<Guid, bool>>(
                new Dictionary<Guid, bool>
                {
                    [waitlistedPlayer.Id] = true,
                    [secondWaitlistedPlayer.Id] = true
                }));

        result.PromotedPlayerProfileId.Should().BeNull();
        (await db.RsvpResponses.AnyAsync(x => x.SessionId == session.Id && x.PlayerProfileId == secondWaitlistedPlayer.Id && x.Status == RsvpStatus.Going)).Should().BeFalse();
        (await db.WaitlistEntries.SingleAsync(x => x.SessionId == session.Id && x.PlayerProfileId == secondWaitlistedPlayer.Id)).Status.Should().Be(WaitlistEntryStatus.Active);
        var outboxMessages = await db.OutboxMessages.Where(x => x.MessageType == "PlayerWaitlistPromoted").ToArrayAsync();
        outboxMessages.Should().NotContain(x => GetPayloadGuid(x.PayloadJson, "SessionId") == session.Id);
    }

    [Fact]
    public async Task RecordCheckInAsync_WhenLateOverrideReasonSupplied_WritesCheckInOverrideAuditRow()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (session, player, _) = await SeedSessionAsync(db, capacity: 1);
        var repository = scope.ServiceProvider.GetRequiredService<IRsvpRepository>();
        await repository.SubmitRsvpAsync(session.Id, player.Id, RsvpStatus.Going);
        var checkedInAtUtc = Utc(2026, 7, 7, 19, 50);

        var result = await repository.RecordCheckInAsync(
            session.Id,
            player.Id,
            player.Id,
            checkedInAtUtc,
            AttendanceOutcome.Late,
            " traffic at gate ");

        result.AdminOverrideId.Should().NotBeNull();
        result.LateOverrideReason.Should().Be("traffic at gate");
        var adminOverride = await db.AdminOverrides.SingleAsync(x => x.Id == result.AdminOverrideId);
        adminOverride.SessionId.Should().Be(session.Id);
        adminOverride.PlayerProfileId.Should().Be(player.Id);
        adminOverride.AdminPlayerProfileId.Should().Be(player.Id);
        adminOverride.OverrideType.Should().Be(AdminOverrideType.CheckIn);
        adminOverride.Reason.Should().Be("traffic at gate");
        adminOverride.AppliedAtUtc.Should().Be(checkedInAtUtc);
        (await db.RsvpResponses.SingleAsync(x => x.SessionId == session.Id && x.PlayerProfileId == player.Id)).Status.Should().Be(RsvpStatus.Going);
    }

    [Fact]
    public async Task RecordNoShowsAsync_WhenConfirmedPlayerNotCheckedIn_RecordsNoShowWithoutChangingRsvp()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (session, player, _) = await SeedSessionAsync(db, capacity: 1);
        var repository = scope.ServiceProvider.GetRequiredService<IRsvpRepository>();
        await repository.SubmitRsvpAsync(session.Id, player.Id, RsvpStatus.Going);

        var count = await repository.RecordNoShowsAsync(session.Id, Utc(2026, 7, 8, 4, 0));

        count.Should().Be(1);
        (await db.RsvpResponses.SingleAsync(x => x.SessionId == session.Id && x.PlayerProfileId == player.Id)).Status.Should().Be(RsvpStatus.Going);
        (await db.CheckIns.SingleAsync(x => x.SessionId == session.Id && x.PlayerProfileId == player.Id)).Outcome.Should().Be(AttendanceOutcome.NoShow);
    }

    [Fact]
    public async Task SubmitRsvpAsync_WhenTwoPlayersRaceForTheLastSpot_ConfirmsExactlyOneAndWaitlistsTheOther()
    {
        using var provider = CreateServiceProvider();
        using var seedScope = provider.CreateScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (session, playerOne, playerTwo) = await SeedSessionAsync(seedDb, capacity: 1);

        // Each racer needs its own scope: DbContext is not thread-safe, and the serializable
        // transaction only proves anything when the two writers are genuinely concurrent.
        var results = await Task.WhenAll(
            SubmitInOwnScopeAsync(provider, session.Id, playerOne.Id),
            SubmitInOwnScopeAsync(provider, session.Id, playerTwo.Id));

        results.Count(x => x == RsvpMutationState.Going).Should().Be(1);
        results.Count(x => x == RsvpMutationState.Going || x == RsvpMutationState.Waitlisted)
            .Should().BeGreaterThanOrEqualTo(1);
        (await seedDb.RsvpResponses.CountAsync(x => x.SessionId == session.Id && x.Status == RsvpStatus.Going))
            .Should().Be(1, "capacity is 1 and the serializable transaction must never admit two");
        (await seedDb.WaitlistEntries.CountAsync(x => x.SessionId == session.Id && x.Status == WaitlistEntryStatus.Active))
            .Should().Be(1);
    }

    [Fact]
    public async Task GetGameDayAttendanceBatchAsync_WhenSessionsHaveMixedAttendance_MatchesPerSessionResults()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (sessionOne, playerOne, playerTwo) = await SeedSessionAsync(db, capacity: 1);
        var (sessionTwo, _, _) = await SeedSessionAsync(db, capacity: 5);
        var repository = scope.ServiceProvider.GetRequiredService<IRsvpRepository>();
        await repository.SubmitRsvpAsync(sessionOne.Id, playerOne.Id, RsvpStatus.Going);
        await repository.SubmitRsvpAsync(sessionOne.Id, playerTwo.Id, RsvpStatus.Going);

        var batch = await repository.GetGameDayAttendanceBatchAsync(
            [sessionOne.Id, sessionTwo.Id],
            playerOne.Id);

        batch.Should().ContainKeys(
            new[] { sessionOne.Id, sessionTwo.Id },
            "the batch contract returns an entry for every requested session, including empty ones");
        batch[sessionOne.Id].Should().BeEquivalentTo(
            await repository.GetGameDayAttendanceAsync(sessionOne.Id, playerOne.Id));
        batch[sessionTwo.Id].Should().BeEquivalentTo(
            await repository.GetGameDayAttendanceAsync(sessionTwo.Id, playerOne.Id));
        batch[sessionOne.Id].GoingCount.Should().Be(1);
        batch[sessionOne.Id].IsCurrentPlayerGoing.Should().BeTrue();
        batch[sessionTwo.Id].GoingCount.Should().Be(0);
    }

    [Fact]
    public async Task CancelAndPromoteAsync_WhenWaitlistHasCandidates_ChecksEligibilityOnceForAllOfThem()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (session, confirmedPlayer, firstCandidate) = await SeedSessionAsync(db, capacity: 1);
        var secondCandidate = CreatePlayer("Kemi");
        await db.PlayerProfiles.AddAsync(secondCandidate);
        await db.SaveChangesAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IRsvpRepository>();
        await repository.SubmitRsvpAsync(session.Id, confirmedPlayer.Id, RsvpStatus.Going);
        await repository.SubmitRsvpAsync(session.Id, firstCandidate.Id, RsvpStatus.Going);
        await repository.SubmitRsvpAsync(session.Id, secondCandidate.Id, RsvpStatus.Going);

        // One batched call must cover the whole waitlist: the per-candidate query this replaced ran
        // inside the serializable transaction, so its cost scaled with waitlist depth.
        var eligibilityCallCount = 0;
        var checkedPlayerProfileIds = new List<Guid>();
        var result = await repository.CancelAndPromoteAsync(
            session.Id,
            confirmedPlayer.Id,
            (candidateIds, _) =>
            {
                eligibilityCallCount++;
                checkedPlayerProfileIds.AddRange(candidateIds);
                return Task.FromResult<IReadOnlyDictionary<Guid, bool>>(
                    candidateIds.ToDictionary(id => id, id => id == secondCandidate.Id));
            });

        eligibilityCallCount.Should().Be(1);
        checkedPlayerProfileIds.Should().BeEquivalentTo([firstCandidate.Id, secondCandidate.Id]);
        result.PromotedPlayerProfileId.Should().Be(secondCandidate.Id);
        (await db.WaitlistEntries.SingleAsync(x => x.SessionId == session.Id && x.PlayerProfileId == firstCandidate.Id))
            .Status.Should().Be(WaitlistEntryStatus.Expired, "an ineligible candidate ahead in the queue is expired");
    }

    private static async Task<RsvpMutationState?> SubmitInOwnScopeAsync(
        ServiceProvider provider,
        Guid sessionId,
        Guid playerProfileId)
    {
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRsvpRepository>();
        try
        {
            var result = await repository.SubmitRsvpAsync(sessionId, playerProfileId, RsvpStatus.Going);
            return result.State;
        }
        catch (ApplicationConflictException)
        {
            // Two serializable writers on one session can deadlock past the retry budget. Losing
            // that way is correct behaviour, not a test failure: the invariant under test is that
            // nobody is admitted beyond capacity.
            return null;
        }
    }

    private ServiceProvider CreateServiceProvider()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Utc(2026, 7, 7, 16, 0));
        var services = new ServiceCollection();
        services.AddSingleton(clock.Object);
        services.AddInfrastructure(database.ConnectionString);
        return services.BuildServiceProvider();
    }

    private static async Task<(Session Session, PlayerProfile PlayerOne, PlayerProfile PlayerTwo)> SeedSessionAsync(
        SouthBaySoccerDbContext db,
        int capacity)
    {
        var season = new Season
        {
            Id = Guid.NewGuid(),
            Name = $"Season {Guid.NewGuid():N}",
            StartsAtUtc = Utc(2026, 1, 1, 0, 0),
            EndsAtUtc = Utc(2026, 12, 31, 23, 0),
        };
        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            Name = $"Venue {Guid.NewGuid():N}",
            Locality = "Torrance",
        };
        var session = new Session
        {
            Id = Guid.NewGuid(),
            SeasonId = season.Id,
            VenueId = venue.Id,
            Title = "Tuesday Pickup",
            Format = "7v7",
            Capacity = capacity,
            TeamCount = 2,
            StartsAtUtc = Utc(2026, 7, 7, 20, 0),
            CheckInOpensAtUtc = Utc(2026, 7, 7, 19, 30),
            CheckInClosesAtUtc = Utc(2026, 7, 7, 19, 45),
            RsvpDeadlineUtc = Utc(2026, 7, 7, 18, 0),
            Status = SessionStatus.Published,
        };
        var playerOne = CreatePlayer("Ada");
        var playerTwo = CreatePlayer("Tunde");

        await db.Seasons.AddAsync(season);
        await db.Venues.AddAsync(venue);
        await db.PlayerProfiles.AddRangeAsync(playerOne, playerTwo);
        await db.Sessions.AddAsync(session);
        await db.SaveChangesAsync();

        return (session, playerOne, playerTwo);
    }

    private static Guid GetPayloadGuid(string payloadJson, string propertyName)
    {
        using var payload = JsonDocument.Parse(payloadJson);
        return payload.RootElement.GetProperty(propertyName).GetGuid();
    }

    private static PlayerProfile CreatePlayer(string displayName) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = $"{displayName} {Guid.NewGuid():N}",
        NormalizedDisplayName = displayName.ToUpperInvariant(),
        PreferredPosition = "Midfielder",
        Role = PlayerRole.Player,
    };

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
