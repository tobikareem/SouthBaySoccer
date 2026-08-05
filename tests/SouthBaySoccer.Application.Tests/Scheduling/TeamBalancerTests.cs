using FluentAssertions;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class TeamBalancerTests
{
    [Fact]
    public void Balance_SameInputsAndSeed_ProducesIdenticalDeal()
    {
        var (teams, players) = Fixture(teamCount: 3, playerCount: 16);

        var first = TeamBalancer.Balance(teams, players, seed: 42);
        var second = TeamBalancer.Balance(teams, players, seed: 42);

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Balance_PermutedInputOrder_ProducesIdenticalDeal()
    {
        var (teams, players) = Fixture(teamCount: 3, playerCount: 16);
        var permuted = players.Reverse().ToArray();

        var first = TeamBalancer.Balance(teams, players, seed: 7);
        var second = TeamBalancer.Balance(teams, permuted, seed: 7);

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Balance_DifferentSeeds_DealTieHeavyRosterDifferently()
    {
        // Every non-captain shares one score, so the deal is purely seed-driven.
        var (teams, players) = Fixture(teamCount: 3, playerCount: 15, uniformScore: 5m);

        var deals = Enumerable.Range(0, 10)
            .Select(attempt => TeamBalancer.Balance(teams, players, seed: attempt))
            .Select(deal => string.Join("|", deal.OrderBy(pair => pair.Key).Select(pair => string.Join(",", pair.Value))))
            .Distinct()
            .ToArray();

        deals.Length.Should().BeGreaterThan(1, "shuffling with different seeds should re-deal tied players");
    }

    [Fact]
    public void Balance_CaptainsAlwaysStayOnTheirOwnTeams()
    {
        // Captain scores at the extremes must not dislodge them.
        var teams = new[]
        {
            new TeamBalancerSeed(Guid.NewGuid(), Guid.NewGuid(), 5),
            new TeamBalancerSeed(Guid.NewGuid(), Guid.NewGuid(), 5),
            new TeamBalancerSeed(Guid.NewGuid(), Guid.NewGuid(), 5),
        };
        var players = new List<TeamBalancerPlayer>
        {
            new(teams[0].CaptainPlayerProfileId, 10m),
            new(teams[1].CaptainPlayerProfileId, 0.5m),
            new(teams[2].CaptainPlayerProfileId, 5m),
        };
        players.AddRange(Enumerable.Range(0, 12).Select(i => new TeamBalancerPlayer(Guid.NewGuid(), (i % 10) + 0.25m)));

        var deal = TeamBalancer.Balance(teams, players, seed: 3);

        foreach (var team in teams)
        {
            deal[team.TeamId].Should().Contain(team.CaptainPlayerProfileId);
        }
    }

    [Fact]
    public void Balance_SixteenPlayersAcrossThreeTeams_FillsCapsExactly()
    {
        // Cap parity fixture: 16 eligible / 3 teams => 6/5/5 with the extra on the 1st-ranked team.
        var caps = GameDayWorkflowQueries.ComputeTeamCaps(totalEligible: 16, teamCount: 3);
        caps.Should().Equal(6, 5, 5);

        var (teams, players) = Fixture(teamCount: 3, playerCount: 16, capsOverride: caps);
        var deal = TeamBalancer.Balance(teams, players, seed: 1);

        deal[teams[0].TeamId].Should().HaveCount(6);
        deal[teams[1].TeamId].Should().HaveCount(5);
        deal[teams[2].TeamId].Should().HaveCount(5);
        deal.SelectMany(pair => pair.Value).Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(players.Select(player => player.PlayerProfileId), "everyone is dealt exactly once");
    }

    [Fact]
    public void Balance_ProducesTighterOrEqualSpreadThanPlainSnake()
    {
        // Skewed roster: the swap loop must never leave the teams worse than the greedy snake.
        var (teams, players) = Fixture(teamCount: 2, playerCount: 10);
        var scored = players
            .Select((player, index) => player with { Score = index < 3 ? 9.5m : 2m + index * 0.1m })
            .ToArray();

        var deal = TeamBalancer.Balance(teams, scored, seed: 11);

        var scoresById = scored.ToDictionary(player => player.PlayerProfileId, player => player.Score);
        var averages = deal.Values
            .Select(ids => ids.Average(id => scoresById[id]))
            .ToArray();
        (averages.Max() - averages.Min()).Should().BeLessThan(2m, "the improvement loop narrows the spread");
    }

    [Fact]
    public void Balance_CaptainsOnlyRoster_ReturnsCaptainsAlone()
    {
        var (teams, players) = Fixture(teamCount: 2, playerCount: 2);

        var deal = TeamBalancer.Balance(teams, players, seed: 5);

        deal.Values.Should().OnlyContain(ids => ids.Count == 1);
    }

    [Fact]
    public void Balance_AllEqualScores_TerminatesImmediately()
    {
        var (teams, players) = Fixture(teamCount: 4, playerCount: 20, uniformScore: 6m);

        var deal = TeamBalancer.Balance(teams, players, seed: 9);

        deal.SelectMany(pair => pair.Value).Should().HaveCount(20);
    }

    [Fact]
    public void Balance_WhenCaptainMissingFromPlayers_Throws()
    {
        var teams = new[] { new TeamBalancerSeed(Guid.NewGuid(), Guid.NewGuid(), 3) };

        var act = () => TeamBalancer.Balance(teams, [new TeamBalancerPlayer(Guid.NewGuid(), 5m)], seed: 0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DeriveSeed_IsStableAndAttemptSensitive()
    {
        var matchId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");

        TeamBalancer.DeriveSeed(matchId, 1).Should().Be(TeamBalancer.DeriveSeed(matchId, 1));
        TeamBalancer.DeriveSeed(matchId, 1).Should().NotBe(TeamBalancer.DeriveSeed(matchId, 2));
    }

    [Fact]
    public void ResolveDraftTurn_WalksTheSnakeAndSkipsFullTeams()
    {
        var teams = new[] { Team(1), Team(2), Team(3) };
        // Caps 3/2/2 => non-captain slots 2/1/1.
        var caps = new[] { 3, 2, 2 };

        // No picks yet: 1st captain is on the clock.
        GameDayWorkflowQueries.ResolveDraftTurn(teams, caps, [0, 0, 0]).OnTheClockTeamId.Should().Be(teams[0].Id);
        // After 1 picks, 2 is up; after 2 picks, 3 is up.
        GameDayWorkflowQueries.ResolveDraftTurn(teams, caps, [1, 0, 0]).OnTheClockTeamId.Should().Be(teams[1].Id);
        GameDayWorkflowQueries.ResolveDraftTurn(teams, caps, [1, 1, 0]).OnTheClockTeamId.Should().Be(teams[2].Id);
        // Snake reversal: after 3 picks the sequence returns 3, 2, 1 — but 3 and 2 are now full,
        // so team 1 takes the remaining slot.
        GameDayWorkflowQueries.ResolveDraftTurn(teams, caps, [1, 1, 1]).OnTheClockTeamId.Should().Be(teams[0].Id);
        // Everyone full: draft complete.
        GameDayWorkflowQueries.ResolveDraftTurn(teams, caps, [2, 1, 1]).OnTheClockTeamId.Should().BeNull();
    }

    [Fact]
    public void ResolveDraftTurn_SecondRoundRunsInReverseOrder()
    {
        var teams = new[] { Team(1), Team(2) };
        var caps = new[] { 3, 3 };

        // Round 1: 1 then 2. Round 2 (reversed): 2 then 1.
        GameDayWorkflowQueries.ResolveDraftTurn(teams, caps, [1, 1]).OnTheClockTeamId.Should().Be(teams[1].Id);
        GameDayWorkflowQueries.ResolveDraftTurn(teams, caps, [1, 2]).OnTheClockTeamId.Should().Be(teams[0].Id);
    }

    private static MatchTeam Team(int number) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = Guid.NewGuid(),
        TeamNumber = number,
        Name = $"Team {number}",
        CaptainPlayerProfileId = Guid.NewGuid(),
    };

    private static (TeamBalancerSeed[] Teams, TeamBalancerPlayer[] Players) Fixture(
        int teamCount,
        int playerCount,
        decimal? uniformScore = null,
        IReadOnlyList<int>? capsOverride = null)
    {
        var caps = capsOverride ?? GameDayWorkflowQueries.ComputeTeamCaps(playerCount, teamCount);
        var captains = Enumerable.Range(0, teamCount)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var teams = captains
            .Select((captainId, index) => new TeamBalancerSeed(Guid.NewGuid(), captainId, caps[index]))
            .ToArray();
        var players = captains
            .Select((captainId, index) => new TeamBalancerPlayer(captainId, uniformScore ?? 4m + index))
            .Concat(Enumerable.Range(0, playerCount - teamCount)
                .Select(i => new TeamBalancerPlayer(
                    Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}"),
                    uniformScore ?? 1m + (i * 7 % 19) * 0.5m)))
            .ToArray();
        return (teams, players);
    }
}
