namespace SouthBaySoccer.Contracts.Sessions;

public enum SessionCardStatus
{
    Open,
    Going,
    Waitlisted,
    Full,
    Closed,
    Canceled
}

public sealed record SessionsDashboardDto(
    string GroupLabel,
    string Greeting,
    string DuesStatus,
    SessionSummaryDto? FeaturedSession,
    StatsPromptDto? StatsPrompt,
    string ComingUpLabel,
    string ScheduleActionLabel,
    IReadOnlyList<SessionSummaryDto> ComingUpSessions,
    bool CanManageSessions = false);

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
    string? RelativeLabel,
    bool IsCanceled = false,
    string? DeadlineLabel = null,
    bool IsGoing = false,
    bool IsWaitlisted = false,
    bool CanJoinWaitlist = false,
    bool IsRsvpClosed = false)
{
    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(Venue)
            ? Title
            : string.IsNullOrWhiteSpace(Format)
                ? Venue
                : $"{Venue} · {Format}";

    public SessionCardStatus CardStatus =>
        IsCanceled ? SessionCardStatus.Canceled
            : IsGoing ? SessionCardStatus.Going
            : IsWaitlisted ? SessionCardStatus.Waitlisted
            : IsFull ? SessionCardStatus.Full
            : IsRsvpClosed ? SessionCardStatus.Closed
            : SessionCardStatus.Open;

    public string CardSemanticDescription => $"{DisplayTitle} — {StatusLabel}";

    public string WaitlistActionDescription => $"Join the waitlist for {DisplayTitle}";
}

public sealed record StatsPromptDto(
    Guid MatchId,
    string Title,
    string Caption,
    Guid SessionId = default,
    bool RequiresClaim = false);

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
    bool IsGoing,
    bool IsCanceled = false);
