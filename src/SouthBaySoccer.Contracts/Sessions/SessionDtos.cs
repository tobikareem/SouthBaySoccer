namespace SouthBaySoccer.Contracts.Sessions;

public sealed record SessionsDashboardDto(
    string GroupLabel,
    string Greeting,
    string DuesStatus,
    SessionSummaryDto FeaturedSession,
    StatsPromptDto StatsPrompt,
    string ComingUpLabel,
    string ScheduleActionLabel,
    IReadOnlyList<SessionSummaryDto> ComingUpSessions);

public sealed record SessionSummaryDto(
    Guid Id,
    string Title,
    string Venue,
    string Format,
    DateTime StartsAtUtc,
    string DateLabel,
    string TimeLabel,
    string StatusLabel,
    int GoingCount,
    int Capacity,
    bool IsFull,
    int WaitlistCount,
    string? RelativeLabel);

public sealed record StatsPromptDto(
    Guid MatchId,
    string Title,
    string Caption);

public sealed record SessionDetailDto(
    Guid Id,
    string Eyebrow,
    string Venue,
    string LocationLabel,
    string Format,
    DateTime StartsAtUtc,
    string DateTimeLabel,
    int GoingCount,
    int Capacity,
    string DeadlineLabel,
    bool IsFull,
    bool IsRsvpAvailable,
    bool IsGoing);
