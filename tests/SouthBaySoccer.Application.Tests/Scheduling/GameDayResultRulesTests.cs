using FluentAssertions;
using SouthBaySoccer.Application.Features.Scheduling;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Scheduling;

/// <summary>
/// Aggregate W/D/L counters can only be checked against the identities every fixture list obeys:
/// each game is one win and one loss, or two halves of a draw.
/// </summary>
public sealed class GameDayResultRulesTests
{
    [Fact]
    public void AreComplete_WhenAThreeTeamRotationPlaysMoreGamesThanOpponents_IsStillComplete()
    {
        // Winner-stays-on: seven games across three teams, well past the old "teamCount - 1" cap.
        var results = new[]
        {
            Result("Green", wins: 3, draws: 1, losses: 1),
            Result("White", wins: 2, draws: 0, losses: 3),
            Result("Spring", wins: 1, draws: 1, losses: 2),
        };

        GameDayResultRules.AreComplete(results, teamCount: 3).Should().BeTrue();
        GameDayResultRules.AreConsistent(results).Should().BeTrue();
    }

    [Fact]
    public void AreComplete_WhenATeamHasNotReported_IsNotComplete()
    {
        var results = new[] { Result("Green", 1, 0, 0) };

        GameDayResultRules.AreComplete(results, teamCount: 2).Should().BeFalse();
    }

    [Fact]
    public void AreComplete_WhenNoGamesWereRecorded_IsNotComplete()
    {
        var results = new[] { Result("Green", 0, 0, 0), Result("White", 0, 0, 0) };

        GameDayResultRules.AreComplete(results, teamCount: 2).Should().BeFalse();
    }

    [Fact]
    public void AreConsistent_WhenWinsDoNotBalanceLosses_IsRejected()
    {
        // Four wins claimed but only two losses: someone's night did not happen.
        var results = new[]
        {
            Result("Green", wins: 3, draws: 0, losses: 1),
            Result("White", wins: 1, draws: 0, losses: 1),
            Result("Spring", wins: 0, draws: 0, losses: 0),
        };

        GameDayResultRules.AreConsistent(results).Should().BeFalse();
    }

    [Fact]
    public void AreConsistent_WhenDrawTotalIsOdd_IsRejected()
    {
        // A draw is recorded by both sides, so an odd total cannot be reconciled.
        var results = new[]
        {
            Result("Green", wins: 1, draws: 1, losses: 0),
            Result("White", wins: 0, draws: 0, losses: 1),
            Result("Spring", wins: 0, draws: 0, losses: 0),
        };

        GameDayResultRules.AreConsistent(results).Should().BeFalse();
    }

    [Theory]
    [InlineData(3, 0, 0, 0, 0, 3, true)]
    [InlineData(2, 1, 0, 0, 1, 2, true)]
    [InlineData(2, 0, 1, 0, 0, 2, false)]
    [InlineData(1, 1, 0, 1, 0, 1, false)]
    public void AreConsistent_WithTwoTeams_RequiresTheRecordsToMirror(
        int firstWins,
        int firstDraws,
        int firstLosses,
        int secondWins,
        int secondDraws,
        int secondLosses,
        bool expected)
    {
        var results = new[]
        {
            Result("Green", firstWins, firstDraws, firstLosses),
            Result("White", secondWins, secondDraws, secondLosses),
        };

        GameDayResultRules.AreConsistent(results).Should().Be(expected);
    }

    [Fact]
    public void AreConsistent_WhenACounterIsNegative_IsRejected()
    {
        var results = new[] { Result("Green", -1, 0, 1), Result("White", 1, 0, -1) };

        GameDayResultRules.AreConsistent(results).Should().BeFalse();
    }

    private static GameDayTeamResultModel Result(string name, int wins, int draws, int losses) =>
        new(Guid.NewGuid(), name, wins, draws, losses);
}
