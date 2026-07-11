namespace SouthBaySoccer.Contracts.Players;

/// <summary>
/// Deliberately excludes any identity/account identifier: this DTO is returned in public-facing
/// listings (players directory, leaderboards, rosters), and navigation between screens uses
/// <see cref="Id"/> (the player profile id), so an identity id has no reason to leave the server.
/// </summary>
public sealed record PlayerSummaryDto(
    Guid Id,
    string DisplayName,
    string Initials,
    string Position,
    bool IsGuest);
