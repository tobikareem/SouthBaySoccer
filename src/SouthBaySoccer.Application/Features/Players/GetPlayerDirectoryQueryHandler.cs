using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Players;

public sealed class GetPlayerDirectoryQueryHandler(IPlayerProfileRepository playerProfileRepository)
{
    public async Task<PlayerDirectoryModel> HandleAsync(CancellationToken cancellationToken = default)
    {
        var rows = await playerProfileRepository.ListDirectoryAsync(cancellationToken);
        var players = rows
            .Select((row, index) => new PlayerDirectoryEntryModel(
                new PlayerDirectorySummaryModel(
                    row.PlayerProfileId,
                    row.DisplayName,
                    BuildInitials(row.DisplayName),
                    row.PreferredPosition,
                    row.IsGuest,
                    row.IdentityUserId),
                $"{(row.IsGuest ? "Guest" : row.PreferredPosition)} \u00B7 #{index + 1}",
                row.Matches))
            .ToArray();

        return new PlayerDirectoryModel(
            "Players",
            "Search the crew and open career stats.",
            players.Length,
            players);
    }

    private static string BuildInitials(string displayName)
    {
        var initials = string.Concat(
            displayName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0])));

        return string.IsNullOrWhiteSpace(initials) ? "?" : initials;
    }
}
