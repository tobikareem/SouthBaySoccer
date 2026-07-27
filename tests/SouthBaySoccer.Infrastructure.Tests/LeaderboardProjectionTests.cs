using FluentAssertions;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Infrastructure.Repositories;
using Xunit;

namespace SouthBaySoccer.Infrastructure.Tests;

/// <summary>
/// Pins the leaderboard ordering contract. Deliberately has no database collection: the ranking
/// rules moved out of SQL into <see cref="LeaderboardProjection"/> when the aggregate queries were
/// flattened, and they must stay verifiable on any machine.
/// </summary>
public sealed class LeaderboardProjectionTests
{
    [Fact]
    public void Order_WhenMetricIsGoals_RanksByGoalsThenFewerAppearancesThenMoreAssists()
    {
        var prolific = Aggregate("Prolific", goals: 5, appearances: 9, assists: 1);
        var efficient = Aggregate("Efficient", goals: 5, appearances: 4, assists: 0);
        var creative = Aggregate("Creative", goals: 5, appearances: 4, assists: 3);
        var quiet = Aggregate("Quiet", goals: 1, appearances: 4, assists: 9);

        var ordered = LeaderboardProjection.Order([prolific, efficient, creative, quiet], StatLeaderboardMetric.Goals);

        ordered.Select(x => x.DisplayName).Should().Equal("Creative", "Efficient", "Prolific", "Quiet");
    }

    [Fact]
    public void Order_WhenMetricIsAssists_RanksByAssistsThenFewerMinutesThenMoreGoals()
    {
        var worker = Aggregate("Worker", assists: 4, minutesPlayed: 300, goals: 9);
        var sharp = Aggregate("Sharp", assists: 4, minutesPlayed: 120, goals: 1);
        var sharper = Aggregate("Sharper", assists: 4, minutesPlayed: 120, goals: 6);

        var ordered = LeaderboardProjection.Order([worker, sharp, sharper], StatLeaderboardMetric.Assists);

        ordered.Select(x => x.DisplayName).Should().Equal("Sharper", "Sharp", "Worker");
    }

    [Fact]
    public void Order_WhenMetricIsRating_RanksByAverageThenVoteCountThenAppearances()
    {
        var unproven = Aggregate("Unproven", averageRating: 4.8m, ratingVoteCount: 2, appearances: 2);
        var proven = Aggregate("Proven", averageRating: 4.8m, ratingVoteCount: 20, appearances: 2);
        var lower = Aggregate("Lower", averageRating: 4.1m, ratingVoteCount: 40, appearances: 30);

        var ordered = LeaderboardProjection.Order([unproven, proven, lower], StatLeaderboardMetric.Rating);

        ordered.Select(x => x.DisplayName).Should().Equal("Proven", "Unproven", "Lower");
    }

    [Fact]
    public void Order_WhenMetricIsMvp_RanksByAwardsThenFewerAppearancesThenRating()
    {
        var veteran = Aggregate("Veteran", mvpAwards: 3, appearances: 20, averageRating: 4.0m);
        var rookie = Aggregate("Rookie", mvpAwards: 3, appearances: 5, averageRating: 3.0m);
        var rated = Aggregate("Rated", mvpAwards: 3, appearances: 5, averageRating: 4.9m);

        var ordered = LeaderboardProjection.Order([veteran, rookie, rated], StatLeaderboardMetric.Mvp);

        ordered.Select(x => x.DisplayName).Should().Equal("Rated", "Rookie", "Veteran");
    }

    [Fact]
    public void Order_WhenEveryRankingKeyTies_FallsBackToDisplayNameThenPlayerProfileId()
    {
        var lastId = Aggregate("Same Name", goals: 2);
        lastId.PlayerProfileId = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var firstId = Aggregate("Same Name", goals: 2);
        firstId.PlayerProfileId = new Guid("11111111-1111-1111-1111-111111111111");
        var laterName = Aggregate("Zed", goals: 2);

        var ordered = LeaderboardProjection.Order([laterName, lastId, firstId], StatLeaderboardMetric.Goals);

        ordered.Select(x => x.PlayerProfileId)
            .Should().Equal(firstId.PlayerProfileId, lastId.PlayerProfileId, laterName.PlayerProfileId);
    }

    [Fact]
    public void Order_WhenNamesDifferOnlyByCase_DoesNotDependOnOrdinalCasing()
    {
        var upper = Aggregate("ZARA", goals: 1);
        var lower = Aggregate("adam", goals: 1);

        var ordered = LeaderboardProjection.Order([upper, lower], StatLeaderboardMetric.Goals);

        ordered.Select(x => x.DisplayName).Should().Equal("adam", "ZARA");
    }

    [Theory]
    [InlineData(StatLeaderboardMetric.Goals, 7)]
    [InlineData(StatLeaderboardMetric.Assists, 3)]
    [InlineData(StatLeaderboardMetric.Mvp, 2)]
    public void GetMetricValue_ForCountMetrics_ReturnsThatCount(StatLeaderboardMetric metric, int expected)
    {
        var row = Aggregate("Ada", goals: 7, assists: 3, mvpAwards: 2, averageRating: 4.25m);

        LeaderboardProjection.GetMetricValue(row, metric).Should().Be(expected);
    }

    [Fact]
    public void GetMetricValue_ForRating_ReturnsTheAverageUnrounded()
    {
        var row = Aggregate("Ada", averageRating: 4.25m);

        LeaderboardProjection.GetMetricValue(row, StatLeaderboardMetric.Rating).Should().Be(4.25m);
    }

    private static PlayerStatAggregate Aggregate(
        string displayName,
        int goals = 0,
        int assists = 0,
        int appearances = 0,
        int minutesPlayed = 0,
        decimal averageRating = 0m,
        int ratingVoteCount = 0,
        int mvpAwards = 0) =>
        new()
        {
            PlayerProfileId = Guid.NewGuid(),
            DisplayName = displayName,
            PreferredPosition = "Midfielder",
            Appearances = appearances,
            MinutesPlayed = minutesPlayed,
            Goals = goals,
            Assists = assists,
            AverageRating = averageRating,
            RatingVoteCount = ratingVoteCount,
            MvpAwards = mvpAwards,
        };
}
