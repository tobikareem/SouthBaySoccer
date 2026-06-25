using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

public sealed class SeedProfileClient(SeedGameDayState? gameDayState = null) : IProfileClient
{
    public Task<PlayerProfileDto?> GetCurrentProfileAsync(CancellationToken cancellationToken) =>
        GetProfileAsync(SeedFixtures.CurrentPlayerId, cancellationToken);

    public Task<PlayerProfileDto?> GetProfileAsync(
        Guid playerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (playerId == SeedFixtures.CurrentPlayerId)
        {
            return Task.FromResult<PlayerProfileDto?>(ApplyRecentForm(SeedFixtures.Profile));
        }

        var playerIndex = FindPlayerIndex(playerId);
        if (playerIndex < 0)
        {
            return Task.FromResult<PlayerProfileDto?>(null);
        }

        return Task.FromResult<PlayerProfileDto?>(ApplyRecentForm(ProfileFor(SeedFixtures.Players[playerIndex], playerIndex)));
    }

    private PlayerProfileDto ApplyRecentForm(PlayerProfileDto profile) =>
        gameDayState is null
            ? profile
            : profile with { RecentForm = gameDayState.RecentFormFor(profile.PlayerId, profile.RecentForm) };
    private static int FindPlayerIndex(Guid playerId)
    {
        for (var index = 0; index < SeedFixtures.Players.Count; index++)
        {
            if (SeedFixtures.Players[index].Id == playerId)
            {
                return index;
            }
        }

        return -1;
    }

    private static PlayerProfileDto ProfileFor(PlayerSummaryDto player, int playerIndex)
    {
        var appearances = Math.Max(6, 24 - playerIndex);
        var goals = Math.Max(0, 14 - playerIndex / 2);
        var assists = Math.Max(0, 10 - playerIndex / 3);
        var rating = Math.Max(6.4m, 8.4m - playerIndex * 0.08m);
        var mvpAwards = Math.Max(0, 4 - playerIndex / 4);
        var likes = Math.Max(6, 44 - playerIndex * 2);

        return new PlayerProfileDto(
            player.Id,
            player.DisplayName,
            $"{player.Position} \u00B7 #{playerIndex + 1}",
            player.Initials,
            new CareerStatsDto(appearances, goals, assists, rating, mvpAwards, likes),
            RecentFormFor(playerIndex),
            null);
    }

    private static IReadOnlyList<MatchResult> RecentFormFor(int playerIndex) =>
        (playerIndex % 3) switch
        {
            0 => [MatchResult.Win, MatchResult.Draw, MatchResult.Win, MatchResult.Win, MatchResult.Loss],
            1 => [MatchResult.Draw, MatchResult.Win, MatchResult.Loss, MatchResult.Win, MatchResult.Draw],
            _ => [MatchResult.Loss, MatchResult.Win, MatchResult.Win, MatchResult.Draw, MatchResult.Win]
        };
}


