using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class PickupPalGameRepositoryTests(InfrastructureDatabaseFixture database)
{
    [Fact]
    public async Task ReplaceParticipantsAsync_WhenIncomingParticipantIsUnresolved_KeepsExistingProfileLink()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IPickupPalGameRepository>();
        var (session, player) = await SeedSessionAsync(db);
        await repository.ReplaceParticipantsAsync(session.Id, [Participant(session.Id, "tob8", playerProfileId: null)]);
        await db.SaveChangesAsync();

        // An admin matches the imported entry to a real profile; the next import still resolves the
        // participant to nothing, because it carries no user id or phone hash.
        var linked = (await repository.ListParticipantsAsync(session.Id)).Single();
        linked.PlayerProfileId = player.Id;
        repository.UpdateParticipant(linked);
        await db.SaveChangesAsync();

        await repository.ReplaceParticipantsAsync(session.Id, [Participant(session.Id, "tob8", playerProfileId: null)]);
        await db.SaveChangesAsync();

        var reimported = (await repository.ListParticipantsAsync(session.Id)).Single();
        reimported.PlayerProfileId.Should().Be(player.Id, "an import must never undo a manual match");
    }

    [Fact]
    public async Task ReplaceParticipantsAsync_WhenIncomingParticipantResolvesToProfile_TakesImportLink()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IPickupPalGameRepository>();
        var (session, player) = await SeedSessionAsync(db);
        await repository.ReplaceParticipantsAsync(session.Id, [Participant(session.Id, "tob8", playerProfileId: null)]);
        await db.SaveChangesAsync();

        await repository.ReplaceParticipantsAsync(session.Id, [Participant(session.Id, "tob8", player.Id)]);
        await db.SaveChangesAsync();

        var reimported = (await repository.ListParticipantsAsync(session.Id)).Single();
        reimported.PlayerProfileId.Should().Be(player.Id);
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

    private static PickupPalGameParticipant Participant(
        Guid sessionId,
        string displayName,
        Guid? playerProfileId) => new()
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            PickupPalParticipantId = "pp-1",
            PlayerProfileId = playerProfileId,
            DisplayName = displayName,
            IsWaitlist = true,
            DisplayOrder = 0,
            JoinedAtUtc = Utc(2026, 7, 7, 15, 0),
        };

    private static async Task<(Session Session, PlayerProfile Player)> SeedSessionAsync(SouthBaySoccerDbContext db)
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
            Capacity = 14,
            TeamCount = 2,
            StartsAtUtc = Utc(2026, 7, 7, 20, 0),
            CheckInOpensAtUtc = Utc(2026, 7, 7, 19, 30),
            CheckInClosesAtUtc = Utc(2026, 7, 7, 19, 45),
            RsvpDeadlineUtc = Utc(2026, 7, 7, 18, 0),
            Status = SessionStatus.Published,
        };
        var player = new PlayerProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = $"Tobi Kareem {Guid.NewGuid():N}",
            NormalizedDisplayName = "TOBI KAREEM",
            PreferredPosition = "Midfielder",
            Role = PlayerRole.Player,
        };

        await db.Seasons.AddAsync(season);
        await db.Venues.AddAsync(venue);
        await db.PlayerProfiles.AddAsync(player);
        await db.Sessions.AddAsync(session);
        await db.SaveChangesAsync();

        return (session, player);
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
