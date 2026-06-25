namespace SouthBaySoccer.Contracts.Players;

public sealed record PlayerDirectoryDto(
    string Title,
    string Subtitle,
    int TotalPlayers,
    IReadOnlyList<PlayerDirectoryEntryDto> Players);

public sealed record PlayerDirectoryEntryDto(
    PlayerSummaryDto Player,
    string Subtitle,
    int Matches);
