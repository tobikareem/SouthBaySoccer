using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
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
            (candidateId, _) => Task.FromResult(candidateId == waitlistedPlayer.Id));

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
            (_, _) => Task.FromResult(true));

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
