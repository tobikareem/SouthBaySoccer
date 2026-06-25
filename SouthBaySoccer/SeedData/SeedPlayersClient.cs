using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

public sealed class SeedPlayersClient : IPlayersClient
{
    public Task<PlayerDirectoryDto> GetDirectoryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var players = SeedFixtures.Players
            .Select((player, index) => new PlayerDirectoryEntryDto(
                player,
                $"{(player.IsGuest ? "Guest" : player.Position)} \u00B7 #{index + 1}",
                Math.Max(6, 24 - index)))
            .ToArray();

        return Task.FromResult(new PlayerDirectoryDto(
            "Players",
            "Search the crew and open career stats.",
            players.Length,
            players));
    }
}
