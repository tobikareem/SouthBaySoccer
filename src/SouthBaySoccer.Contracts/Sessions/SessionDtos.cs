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
    bool IsRsvpClosed = false,
    string? GroupChatName = null,
    Guid? GroupChatId = null,
    string? MembershipStatus = null,
    bool CanJoin = true)
{
    /// <summary>
    /// Whether this session carries a WhatsApp group chat name. Sessions an organizer created by
    /// hand have none, so the schedule card hides the group chip rather than rendering it blank.
    /// </summary>
    public bool HasGroupChatName => !string.IsNullOrWhiteSpace(GroupChatName);

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

    /// <summary>A grouped session requires explicit approval even if an older response defaults CanJoin to true.</summary>
    public bool CanJoinSession => CanJoin && (GroupChatId is null || MembershipStatus == "Approved");

    /// <summary>Visibility and command guard for the waitlist action.</summary>
    public bool ShowJoinWaitlist => CanJoinWaitlist && CanJoinSession;
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
    bool IsCanceled = false,
    Guid? GroupChatId = null,
    string? GroupName = null,
    string? MembershipStatus = null,
    bool CanJoin = true,
    bool IsWaitlisted = false)
{
    /// <summary>A grouped session requires explicit approval even if an older response defaults CanJoin to true.</summary>
    public bool CanJoinSession => CanJoin && (GroupChatId is null || MembershipStatus == "Approved");
}
