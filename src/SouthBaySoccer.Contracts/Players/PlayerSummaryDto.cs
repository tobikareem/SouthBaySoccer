namespace SouthBaySoccer.Contracts.Players;

public sealed record PlayerSummaryDto(
    Guid Id,
    string DisplayName,
    string Initials,
    string Position,
    bool IsGuest,
    Guid? IdentityId);
