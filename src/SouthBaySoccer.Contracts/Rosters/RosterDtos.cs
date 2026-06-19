using SouthBaySoccer.Contracts.Players;

namespace SouthBaySoccer.Contracts.Rosters;

public sealed record RosterDto(
    Guid SessionId,
    IReadOnlyList<RosterEntryDto> Going,
    IReadOnlyList<WaitlistEntryDto> Waitlist);

public sealed record RosterEntryDto(
    PlayerSummaryDto Player,
    bool IsCurrentPlayer);

public sealed record WaitlistEntryDto(
    PlayerSummaryDto Player,
    int Position);
