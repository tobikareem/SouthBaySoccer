using SouthBaySoccer.Application.Abstractions.Caching;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Players;

public sealed class GetPlayerDirectoryQueryHandler(
    IPlayerProfileRepository playerProfileRepository,
    IReadThroughCache cache)
{
    private const string DirectoryCacheKey = "players:directory";
    private static readonly TimeSpan DirectoryTimeToLive = TimeSpan.FromSeconds(60);

    public async Task<PlayerDirectoryModel> HandleAsync(CancellationToken cancellationToken = default)
    {
        // The directory is the same for everyone and tolerates a minute of lag; a new player simply
        // appears on the next refresh.
        var rows = await cache.GetOrCreateAsync(
            DirectoryCacheKey,
            DirectoryTimeToLive,
            playerProfileRepository.ListDirectoryAsync,
            cancellationToken);
        var players = rows
            .Select((row, index) => new PlayerDirectoryEntryModel(
                new PlayerDirectorySummaryModel(
                    row.PlayerProfileId,
                    row.DisplayName,
                    PlayerInitials.Build(row.DisplayName),
                    row.PreferredPosition,
                    row.IsGuest),
                $"{(row.IsGuest ? "Guest" : row.PreferredPosition)} \u00B7 #{index + 1}",
                row.Matches))
            .ToArray();

        return new PlayerDirectoryModel(
            "Players",
            "Search the crew and open career stats.",
            players.Length,
            players);
    }
}
