using FluentAssertions;
using SouthBaySoccer.Contracts.Leaderboards;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.SeedData;

namespace SouthBaySoccer.Client.Tests;

public class SeedLeaderboardAndProfileClientTests
{
    [Fact]
    public async Task GetRankingAsync_AllMetrics_ReturnsStableOrderedRankings()
    {
        var client = new SeedLeaderboardClient();

        var rankings = new List<LeaderboardDto>();
        foreach (var metric in Enum.GetValues<LeaderboardMetric>())
        {
            rankings.Add(
                await client.GetRankingAsync(
                    SeedFixtures.Season2026Id,
                    metric,
                    CancellationToken.None));
        }

        rankings.Select(item => item.Metric).Should().Equal(
            LeaderboardMetric.Goals,
            LeaderboardMetric.Assists,
            LeaderboardMetric.Rating,
            LeaderboardMetric.Mvp);
        rankings.Should().OnlyContain(item => item.SeasonLabel == "Season 2026");
        rankings.Should().OnlyContain(
            item => item.Rows.Select(row => row.Rank).SequenceEqual(new[] { 1, 2, 3, 4, 5 }));
    }


    [Fact]
    public async Task GetProfileAsync_LeaderboardPlayer_ReturnsInspectableCareerStats()
    {
        var client = new SeedProfileClient();
        var player = SeedFixtures.Players[1];

        var profile = await client.GetProfileAsync(player.Id, CancellationToken.None);

        profile.Should().NotBeNull();
        profile!.PlayerId.Should().Be(player.Id);
        profile.DisplayName.Should().Be(player.DisplayName);
        profile.CareerStats.Matches.Should().BeGreaterThan(0);
        profile.CareerStats.AverageRating.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetProfileAsync_CurrentPlayer_ReturnsCompleteWireframeProfile()
    {
        var client = new SeedProfileClient();

        var profile = await client.GetProfileAsync(
            SeedFixtures.CurrentPlayerId,
            CancellationToken.None);

        profile!.DisplayName.Should().Be("Tobi Kareem");
        profile.Subtitle.Should().Be("\"Captain\" · #8");
        profile.CareerStats.Should().Be(
            new CareerStatsDto(24, 12, 9, 7.8m, 3, 41));
        profile.RecentForm.Should().Equal(
            MatchResult.Win,
            MatchResult.Win,
            MatchResult.Draw,
            MatchResult.Win,
            MatchResult.Loss);
        profile.PendingConfirmationNote.Should()
            .Be("2 goals from Sat awaiting confirmation");
    }
}

