namespace SouthBaySoccer.Application.Features.Players;

public sealed record PlayerDirectoryModel(
    string Title,
    string Subtitle,
    int TotalPlayers,
    IReadOnlyList<PlayerDirectoryEntryModel> Players);

public sealed record PlayerDirectoryEntryModel(
    PlayerDirectorySummaryModel Player,
    string Subtitle,
    int Matches);

public sealed record PlayerDirectorySummaryModel(
    Guid Id,
    string DisplayName,
    string Initials,
    string Position,
    bool IsGuest,
    Guid? IdentityId);
