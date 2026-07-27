using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Groups;
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
        var (season, ada, tunde, _) = await SeedGoalLeaderboardAsync(db);

        var rows = await repository.ListSeasonLeaderboardAsync(season.Id, StatLeaderboardMetric.Goals, skip: 0, take: 10, groupChatId: null);

        rows.Select(x => x.PlayerProfileId).Should().StartWith([tunde.Id, ada.Id]);
        rows.Single(x => x.PlayerProfileId == tunde.Id).Goals.Should().Be(2);
        rows.Single(x => x.PlayerProfileId == ada.Id).Goals.Should().Be(2);
        rows.Single(x => x.PlayerProfileId == ada.Id).Assists.Should().Be(1);
        rows.Should().NotContain(x => x.Goals > 2);
    }

    [Fact]
    public async Task ListSeasonLeaderboardAsync_WhenGroupFilterApplied_RestrictsToGroupMembers()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IStatsRepository>();
        var (season, ada, tunde, _) = await SeedGoalLeaderboardAsync(db);

        // Link only Tunde to the group; Ada is a member of no group.
        var group = new GroupChat
        {
            Id = Guid.NewGuid(),
            ExternalId = $"{Guid.NewGuid():N}@g.us",
            GroupName = "Bay Area Soccer",
            Status = "SUBSCRIBED",
        };
        await db.GroupChats.AddAsync(group);
        await db.PlayerGroupLinks.AddAsync(new PlayerGroupLink
        {
            Id = Guid.NewGuid(),
            PlayerProfileId = tunde.Id,
            GroupChatId = group.Id,
            IsPrimary = true,
        });
        await db.SaveChangesAsync();

        var groupRows = await repository.ListSeasonLeaderboardAsync(
            season.Id, StatLeaderboardMetric.Goals, skip: 0, take: 10, groupChatId: group.Id);
        var allRows = await repository.ListSeasonLeaderboardAsync(
            season.Id, StatLeaderboardMetric.Goals, skip: 0, take: 10, groupChatId: null);

        groupRows.Select(x => x.PlayerProfileId).Should().Equal(tunde.Id);
        allRows.Select(x => x.PlayerProfileId).Should().Contain([ada.Id, tunde.Id]);
    }

    [Fact]
    public async Task ListSeasonLeaderboardAsync_AssemblesEveryAggregateFromItsOwnFactTable()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (season, ada, tunde, bola) = await SeedGoalLeaderboardAsync(db);
        var seasonMatchIds = await db.Matches
            .Join(db.Sessions, match => match.SessionId, session => session.Id, (match, session) => new { match, session })
            .Where(x => x.session.SeasonId == season.Id)
            .OrderBy(x => x.match.CompletedAtUtc)
            .Select(x => x.match.Id)
            .ToArrayAsync();
        var firstMatchId = seasonMatchIds[0];
        // Ratings, likes and awards each come from a separate table and are now assembled by their
        // own grouped query, so each needs facts that differ from the others to be distinguishable.
        await db.PlayerRatingVotes.AddRangeAsync(
            new PlayerRatingVote { Id = Guid.NewGuid(), MatchId = firstMatchId, VoterPlayerProfileId = tunde.Id, RatedPlayerProfileId = ada.Id, Score = 8 },
            new PlayerRatingVote { Id = Guid.NewGuid(), MatchId = firstMatchId, VoterPlayerProfileId = bola.Id, RatedPlayerProfileId = ada.Id, Score = 6 });
        await db.PlayerLikes.AddAsync(
            new PlayerLike { Id = Guid.NewGuid(), MatchId = firstMatchId, GiverPlayerProfileId = bola.Id, ReceiverPlayerProfileId = ada.Id });
        await db.MatchAwards.AddAsync(
            new MatchAward { Id = Guid.NewGuid(), MatchId = firstMatchId, PlayerProfileId = ada.Id, AwardType = MatchAwardType.Mvp });
        await db.SaveChangesAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IStatsRepository>();

        var rows = await repository.ListSeasonLeaderboardAsync(
            season.Id,
            StatLeaderboardMetric.Rating,
            skip: 0,
            take: 25,
            groupChatId: null);

        var adaRow = rows.Single(row => row.PlayerProfileId == ada.Id);
        adaRow.Appearances.Should().Be(2);
        adaRow.Goals.Should().Be(2, "the second match's goal is still Pending review");
        adaRow.Assists.Should().Be(1);
        adaRow.AverageRating.Should().Be(7m);
        adaRow.RatingVoteCount.Should().Be(2);
        adaRow.Likes.Should().Be(1);
        adaRow.MvpAwards.Should().Be(1);
        adaRow.Value.Should().Be(7m, "the requested metric is Rating");
        rows.Single(row => row.PlayerProfileId == tunde.Id).AverageRating
            .Should().Be(0m, "a player with no votes must not inherit another player's average");
    }

    [Fact]
    public async Task GetPlayerStatsAsync_ForOnePlayer_MatchesThatPlayersLeaderboardRow()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (season, ada, _, _) = await SeedGoalLeaderboardAsync(db);
        var repository = scope.ServiceProvider.GetRequiredService<IStatsRepository>();

        var summary = await repository.GetPlayerStatsAsync(ada.Id, season.Id);
        var leaderboardRow = (await repository.ListSeasonLeaderboardAsync(
                season.Id,
                StatLeaderboardMetric.Goals,
                skip: 0,
                take: 25,
                groupChatId: null))
            .Single(row => row.PlayerProfileId == ada.Id);

        summary.Should().NotBeNull();
        summary!.Appearances.Should().Be(leaderboardRow.Appearances);
        summary.Goals.Should().Be(leaderboardRow.Goals);
        summary.Assists.Should().Be(leaderboardRow.Assists);
        summary.AverageRating.Should().Be(leaderboardRow.AverageRating);
        summary.Likes.Should().Be(leaderboardRow.Likes);
        summary.MvpAwards.Should().Be(leaderboardRow.MvpAwards);
    }

    [Fact]
    public async Task ListSeasonLeaderboardAsync_WhenPaging_ReturnsDisjointPagesInRankOrder()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (season, _, _, bola) = await SeedGoalLeaderboardAsync(db);
        var repository = scope.ServiceProvider.GetRequiredService<IStatsRepository>();

        var allRows = await repository.ListSeasonLeaderboardAsync(season.Id, StatLeaderboardMetric.Goals, 0, 25, null);
        var firstPage = await repository.ListSeasonLeaderboardAsync(season.Id, StatLeaderboardMetric.Goals, 0, 2, null);
        var secondPage = await repository.ListSeasonLeaderboardAsync(season.Id, StatLeaderboardMetric.Goals, 2, 2, null);

        firstPage.Should().HaveCount(2);
        firstPage.Select(x => x.PlayerProfileId).Should().Equal(allRows.Take(2).Select(x => x.PlayerProfileId));
        secondPage.Select(x => x.PlayerProfileId).Should().Equal(allRows.Skip(2).Take(2).Select(x => x.PlayerProfileId));
        firstPage.Select(x => x.PlayerProfileId).Should().NotIntersectWith(secondPage.Select(x => x.PlayerProfileId));
    }

    [Fact]
    public async Task ListSeasonLeaderboardAsync_WhenScorerDidNotPlayThatMatch_WithholdsCreditAndKeepsThemOffTheBoard()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (season, _, _, bola) = await SeedGoalLeaderboardAsync(db);
        var firstMatchId = await db.Matches
            .Join(db.Sessions, match => match.SessionId, session => session.Id, (match, session) => new { match, session })
            .Where(x => x.session.SeasonId == season.Id)
            .OrderBy(x => x.match.CompletedAtUtc)
            .Select(x => x.match.Id)
            .FirstAsync();
        // A ghost has an approved goal but no Played participation row, which is exactly what the
        // PlayerMatchStats semi-join exists to reject. Every other seeded player plays every match,
        // so without this case the guard could be deleted and the suite would stay green.
        var ghost = CreatePlayer("Ghost Scorer", "Forward");
        await db.PlayerProfiles.AddAsync(ghost);
        await db.MatchEvents.AddAsync(Goal(firstMatchId, ghost.Id, null, MatchEventReviewStatus.Approved));
        await db.SaveChangesAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IStatsRepository>();

        var rows = await repository.ListSeasonLeaderboardAsync(season.Id, StatLeaderboardMetric.Goals, 0, 25, null);

        rows.Should().NotContain(row => row.PlayerProfileId == ghost.Id,
            "the leaderboard is driven by players with a Played participation row");
        var ghostSummary = await repository.GetPlayerStatsAsync(ghost.Id, season.Id);
        ghostSummary!.Goals.Should().Be(0, "a goal in a match the player did not play earns no credit");
    }

    [Fact]
    public async Task ListSeasonLeaderboardAsync_WhenPlayerOnlyScoredAnOwnGoal_CreditsThemWithNoGoals()
    {
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var (season, _, _, bola) = await SeedGoalLeaderboardAsync(db);
        var repository = scope.ServiceProvider.GetRequiredService<IStatsRepository>();

        var rows = await repository.ListSeasonLeaderboardAsync(season.Id, StatLeaderboardMetric.Goals, 0, 25, null);

        // Bola's only seeded event is an OwnGoal; scorer credit must never include it.
        rows.Single(row => row.PlayerProfileId == bola.Id).Goals.Should().Be(0);
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

    private static async Task<(Season Season, PlayerProfile Ada, PlayerProfile Tunde, PlayerProfile Bola)> SeedGoalLeaderboardAsync(SouthBaySoccerDbContext db)
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
        return (season, ada, tunde, bola);
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
