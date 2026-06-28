using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using SoccerMatch = SouthBaySoccer.Domain.Entities.Stats.Match;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure;
using SouthBaySoccer.Infrastructure.Persistence;
using Xunit;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class StatsRepositoryQueryTests
{
    private readonly InfrastructureDatabaseFixture database;

    public StatsRepositoryQueryTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task ListSeasonLeaderboardAsync_WhenGoalsMetricRequested_UsesApprovedGoalsAndTieBreakers()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IStatsRepository>();
        var (season, ada, tunde) = await SeedGoalLeaderboardAsync(db);

        var rows = await repository.ListSeasonLeaderboardAsync(season.Id, StatLeaderboardMetric.Goals, skip: 0, take: 10);

        rows.Select(x => x.PlayerProfileId).Should().StartWith([tunde.Id, ada.Id]);
        rows.Single(x => x.PlayerProfileId == tunde.Id).Goals.Should().Be(2);
        rows.Single(x => x.PlayerProfileId == ada.Id).Goals.Should().Be(2);
        rows.Single(x => x.PlayerProfileId == ada.Id).Assists.Should().Be(1);
        rows.Should().NotContain(x => x.Goals > 2);
    }

    private ServiceProvider CreateServiceProvider()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 7, 7, 16, 0, 0, DateTimeKind.Utc));
        var services = new ServiceCollection();
        services.AddSingleton(clock.Object);
        services.AddInfrastructure(database.ConnectionString);
        return services.BuildServiceProvider();
    }

    private static async Task<(Season Season, PlayerProfile Ada, PlayerProfile Tunde)> SeedGoalLeaderboardAsync(SouthBaySoccerDbContext db)
    {
        var season = new Season { Id = Guid.NewGuid(), Name = $"Season {Guid.NewGuid():N}", StartsAtUtc = Utc(2026, 1, 1), EndsAtUtc = Utc(2026, 12, 31) };
        var otherSeason = new Season { Id = Guid.NewGuid(), Name = $"Season {Guid.NewGuid():N}", StartsAtUtc = Utc(2025, 1, 1), EndsAtUtc = Utc(2025, 12, 31) };
        var venue = new Venue { Id = Guid.NewGuid(), Name = $"Venue {Guid.NewGuid():N}", Locality = "Torrance" };
        var ada = CreatePlayer("Ada Okafor", "Forward");
        var tunde = CreatePlayer("Tunde Bello", "Forward");
        var bola = CreatePlayer("Bola Ade", "Midfielder");
        await db.Seasons.AddRangeAsync(season, otherSeason);
        await db.Venues.AddAsync(venue);
        await db.PlayerProfiles.AddRangeAsync(ada, tunde, bola);

        var sessionOne = CreateSession(season.Id, venue.Id, teamCount: 2, startsAtUtc: Utc(2026, 7, 7));
        var sessionTwo = CreateSession(season.Id, venue.Id, teamCount: 2, startsAtUtc: Utc(2026, 7, 14));
        var otherSession = CreateSession(otherSeason.Id, venue.Id, teamCount: 2, startsAtUtc: Utc(2025, 7, 7));
        await db.Sessions.AddRangeAsync(sessionOne, sessionTwo, otherSession);

        var matchOne = new SoccerMatch { Id = Guid.NewGuid(), SessionId = sessionOne.Id, MatchNumber = 1, Status = MatchStatus.Locked, CompletedAtUtc = Utc(2026, 7, 7, 22) };
        var matchTwo = new SoccerMatch { Id = Guid.NewGuid(), SessionId = sessionTwo.Id, MatchNumber = 1, Status = MatchStatus.Locked, CompletedAtUtc = Utc(2026, 7, 14, 22) };
        var otherMatch = new SoccerMatch { Id = Guid.NewGuid(), SessionId = otherSession.Id, MatchNumber = 1, Status = MatchStatus.Locked, CompletedAtUtc = Utc(2025, 7, 7, 22) };
        await db.Matches.AddRangeAsync(matchOne, matchTwo, otherMatch);

        await AddTeamsAndParticipantsAsync(db, matchOne, ada, tunde, bola);
        await AddTeamsAndParticipantsAsync(db, matchTwo, ada, bola);
        await AddTeamsAndParticipantsAsync(db, otherMatch, ada, tunde, bola);

        await db.MatchEvents.AddRangeAsync(
            Goal(matchOne.Id, ada.Id, bola.Id, MatchEventReviewStatus.Approved),
            Goal(matchOne.Id, ada.Id, null, MatchEventReviewStatus.Approved),
            Goal(matchOne.Id, tunde.Id, ada.Id, MatchEventReviewStatus.Approved),
            Goal(matchOne.Id, tunde.Id, null, MatchEventReviewStatus.Approved),
            Goal(matchTwo.Id, ada.Id, null, MatchEventReviewStatus.Pending),
            new MatchEvent { Id = Guid.NewGuid(), MatchId = matchTwo.Id, EventType = MatchEventType.OwnGoal, PlayerProfileId = bola.Id, ReviewStatus = MatchEventReviewStatus.Approved },
            Goal(otherMatch.Id, ada.Id, null, MatchEventReviewStatus.Approved));

        await db.SaveChangesAsync();
        return (season, ada, tunde);
    }

    private static async Task AddTeamsAndParticipantsAsync(SouthBaySoccerDbContext db, SoccerMatch match, params PlayerProfile[] players)
    {
        var teamOne = new MatchTeam { Id = Guid.NewGuid(), MatchId = match.Id, TeamNumber = 1, Name = "Green" };
        var teamTwo = new MatchTeam { Id = Guid.NewGuid(), MatchId = match.Id, TeamNumber = 2, Name = "White" };
        await db.MatchTeams.AddRangeAsync(teamOne, teamTwo);
        foreach (var player in players)
        {
            var team = player == players[0] ? teamOne : teamTwo;
            await db.TeamAssignments.AddAsync(new TeamAssignment { Id = Guid.NewGuid(), MatchId = match.Id, MatchTeamId = team.Id, PlayerProfileId = player.Id });
            await db.PlayerMatchStats.AddAsync(new PlayerMatchStats { Id = Guid.NewGuid(), MatchId = match.Id, PlayerProfileId = player.Id, Played = true, Started = true, Position = player.PreferredPosition });
        }
    }

    private static MatchEvent Goal(Guid matchId, Guid scorerId, Guid? assistId, MatchEventReviewStatus status) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = matchId,
        EventType = MatchEventType.Goal,
        PlayerProfileId = scorerId,
        AssistPlayerProfileId = assistId,
        ReviewStatus = status,
    };

    private static Session CreateSession(Guid seasonId, Guid venueId, int teamCount, DateTime startsAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        SeasonId = seasonId,
        VenueId = venueId,
        Title = "Pickup",
        Format = "7v7",
        Capacity = 20,
        TeamCount = teamCount,
        StartsAtUtc = startsAtUtc,
        CheckInOpensAtUtc = startsAtUtc.AddMinutes(-30),
        CheckInClosesAtUtc = startsAtUtc.AddMinutes(-15),
        RsvpDeadlineUtc = startsAtUtc.AddHours(-1),
        Status = SessionStatus.Completed,
    };

    private static PlayerProfile CreatePlayer(string displayName, string position) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = displayName,
        NormalizedDisplayName = displayName.ToUpperInvariant(),
        PreferredPosition = position,
        Role = PlayerRole.Player,
    };

    private static DateTime Utc(int year, int month, int day, int hour = 20) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
}
