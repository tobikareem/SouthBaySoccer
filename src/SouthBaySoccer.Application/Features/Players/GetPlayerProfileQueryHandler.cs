using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Players;

public sealed class GetPlayerProfileQueryHandler(
    IPlayerProfileRepository playerProfileRepository,
    IStatsRepository statsRepository)
{
    private const int RecentFormTake = 5;

    public async Task<PlayerProfileDetailModel> HandleAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await playerProfileRepository.FindProfileAsync(playerProfileId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
        var stats = await statsRepository.GetPlayerStatsAsync(playerProfileId, seasonId: null, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
        var recentForm = await statsRepository.ListPlayerRecentFormAsync(
            playerProfileId,
            RecentFormTake,
            cancellationToken);

        return new PlayerProfileDetailModel(
            profile.Id,
            profile.DisplayName,
            profile.PreferredPosition,
            PlayerInitials.Build(profile.DisplayName),
            new CareerStatsModel(
                stats.Appearances,
                stats.Goals,
                stats.Assists,
                stats.AverageRating,
                stats.MvpAwards,
                stats.Likes,
                stats.Wins,
                stats.Losses),
            BuildRecentForm(recentForm),
            profile.IsGuest ? "Guest profile" : null,
            profile.Role.ToString());
    }

    private static IReadOnlyList<PlayerProfileRecentFormOutcome> BuildRecentForm(
        IReadOnlyList<PlayerRecentFormReadModel> rows)
    {
        var outcomes = new List<PlayerProfileRecentFormOutcome>(RecentFormTake);
        foreach (var row in rows)
        {
            outcomes.AddRange(Enumerable.Repeat(PlayerProfileRecentFormOutcome.Win, row.Wins));
            outcomes.AddRange(Enumerable.Repeat(PlayerProfileRecentFormOutcome.Draw, row.Draws));
            outcomes.AddRange(Enumerable.Repeat(PlayerProfileRecentFormOutcome.Loss, row.Losses));
            if (outcomes.Count >= RecentFormTake)
            {
                break;
            }
        }

        return outcomes.Take(RecentFormTake).ToArray();
    }
}
