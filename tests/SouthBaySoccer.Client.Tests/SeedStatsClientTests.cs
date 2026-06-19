using FluentAssertions;
using SouthBaySoccer.Contracts.Stats;
using SouthBaySoccer.SeedData;

namespace SouthBaySoccer.Client.Tests;

public class SeedStatsClientTests
{
    [Fact]
    public async Task SubmitAndConfirmStatsAsync_Commands_UpdateOnlyCurrentSeedState()
    {
        var firstState = new SeedState();
        var firstClient = new SeedStatsClient(firstState);
        var isolatedClient = new SeedStatsClient(new SeedState());
        var playerId = SeedFixtures.MatchStats.TeammateSubmissions[1].Player.Id;

        await firstClient.SubmitStatsAsync(
            SeedFixtures.FeaturedMatchId,
            4,
            3,
            CancellationToken.None);
        await firstClient.ConfirmStatsAsync(
            SeedFixtures.FeaturedMatchId,
            playerId,
            CancellationToken.None);

        var changed = await firstClient.GetMatchStatsAsync(
            SeedFixtures.FeaturedMatchId,
            CancellationToken.None);
        var isolated = await isolatedClient.GetMatchStatsAsync(
            SeedFixtures.FeaturedMatchId,
            CancellationToken.None);

        changed!.Goals.Should().Be(4);
        changed.Assists.Should().Be(3);
        changed.IsPendingConfirmation.Should().BeTrue();
        changed.TeammateSubmissions.Single(item => item.Player.Id == playerId)
            .IsConfirmed.Should().BeTrue();
        isolated!.Goals.Should().Be(2);
        isolated.Assists.Should().Be(1);
        SeedFixtures.MatchStats.Goals.Should().Be(2);
    }

    [Fact]
    public async Task GetRateableTeammatesAsync_CurrentPlayer_ExcludesRaterAndReturnsWireframeValues()
    {
        var client = new SeedStatsClient(new SeedState());

        var teammates = await client.GetRateableTeammatesAsync(
            SeedFixtures.FeaturedMatchId,
            SeedFixtures.CurrentPlayerId,
            CancellationToken.None);

        teammates.Should().HaveCount(3);
        teammates.Should().NotContain(item => item.Player.Id == SeedFixtures.CurrentPlayerId);
        teammates.Select(item => (item.Player.DisplayName, item.Detail, item.Rating))
            .Should()
            .Equal(
                ("Kola T.", "2 goals", 9),
                ("Jide D.", "1 assist", 7),
                ("Sade M.", "clean sheet", 8));
        teammates.Single(item => item.Player.DisplayName == "Jide D.").IsLiked.Should().BeTrue();
        teammates.Single(item => item.Player.DisplayName == "Sade M.").IsMvp.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitRatingsAsync_ValidSubmission_IsVisibleUntilReset()
    {
        var state = new SeedState();
        var client = new SeedStatsClient(state);
        var ratings = SeedFixtures.RateableTeammates
            .Select((item, index) => new TeammateRatingDto(
                item.Player.Id,
                index + 5,
                index == 0,
                index == 1))
            .ToArray();

        var result = await client.SubmitRatingsAsync(
            SeedFixtures.FeaturedMatchId,
            SeedFixtures.CurrentPlayerId,
            ratings,
            CancellationToken.None);
        var changed = await client.GetRateableTeammatesAsync(
            SeedFixtures.FeaturedMatchId,
            SeedFixtures.CurrentPlayerId,
            CancellationToken.None);
        state.Reset();
        var reset = await client.GetRateableTeammatesAsync(
            SeedFixtures.FeaturedMatchId,
            SeedFixtures.CurrentPlayerId,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        changed.Select(item => item.Rating).Should().Equal(5, 6, 7);
        changed.Single(item => item.Player.DisplayName == "Jide D.").IsMvp.Should().BeTrue();
        reset.Select(item => item.Rating).Should().Equal(9, 7, 8);
    }

    [Fact]
    public async Task SubmitRatingsAsync_UnknownPlayer_ReturnsRecoverableFailure()
    {
        var client = new SeedStatsClient(new SeedState());
        var ratings = new[]
        {
            new TeammateRatingDto(
                Guid.Parse("90000000-0000-0000-0000-000000000001"),
                8,
                false,
                false)
        };

        var result = await client.SubmitRatingsAsync(
            SeedFixtures.FeaturedMatchId,
            SeedFixtures.CurrentPlayerId,
            ratings,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("player_not_rateable");
    }
}
