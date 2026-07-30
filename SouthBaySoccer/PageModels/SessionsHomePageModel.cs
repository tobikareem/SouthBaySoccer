using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Page model for the Sessions home screen (SES-6). Loads the dashboard through
/// <see cref="ISessionsClient"/>, maps the result onto loading / content / empty / error /
/// offline view states for <c>StateView</c>, and exposes navigation and join-waitlist commands.
/// Navigation is delegated to <see cref="ISessionsNavigator"/> so this model stays MAUI-free
/// and unit-testable.
/// </summary>
public partial class SessionsHomePageModel(
    ISessionsClient sessionsClient,
    ISessionsNavigator navigator,
    IProfileClient profileClient,
    IGroupsClient groupsClient,
    IDismissedStatsPromptStore dismissedPromptStore,
    IClientResponseCache responseCache,
    TimeProvider timeProvider,
    IAnnouncementsClient? announcementsClient = null,
    IAnnouncementsNavigator? announcementsNavigator = null,
    IUserDialogService? dialogService = null) : ObservableObject
{
    private string? _cachedProfileDisplayName;
    private string? _cachedProfileRole;
    private string? _cachedGroupLabel;
    private Guid _primaryGroupId;

    public const string ErrorTitle = "Couldn't load your sessions";
    public const string ErrorMessage = "Something went wrong loading your home screen. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load your dues status and upcoming sessions.";

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private string _groupLabel = string.Empty;

    [ObservableProperty]
    private string _greeting = string.Empty;

    [ObservableProperty]
    private string _duesStatus = string.Empty;

    [ObservableProperty]
    private SessionSummaryDto? _featuredSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsPromptTitle))]
    [NotifyPropertyChangedFor(nameof(StatsPromptCaption))]
    [NotifyPropertyChangedFor(nameof(StatsPromptSemanticDescription))]
    [NotifyPropertyChangedFor(nameof(StatsPromptSemanticHint))]
    [NotifyPropertyChangedFor(nameof(HasStatsPrompt))]
    [NotifyPropertyChangedFor(nameof(HasLatestMatch))]
    private StatsPromptDto? _statsPrompt;

    /// <summary>
    /// The card shows for any recent game the player might report on - whether they can submit now
    /// or still need to claim their spot first. It stays hidden only when the server sends nothing.
    /// </summary>
    public bool HasStatsPrompt => StatsPrompt is not null;

    /// <summary>True when the server carries a specific match this player can report stats for.</summary>
    public bool HasLatestMatch => StatsPrompt is { MatchId: var matchId } && matchId != Guid.Empty;

    // The server flags a prompt that needs a self-claim first (the player isn't linked to the game
    // yet); tapping it opens Claim your spot rather than the stats form.
    private bool RequiresClaim => StatsPrompt is { RequiresClaim: true };

    public string StatsPromptTitle => StatsPrompt?.Title ?? "Submit your latest stats";

    public string StatsPromptCaption =>
        StatsPrompt?.Caption ?? "Add your goals and assists after the match";

    public string StatsPromptSemanticDescription =>
        $"{StatsPromptTitle}. {StatsPromptCaption}";

    public string StatsPromptSemanticHint =>
        RequiresClaim ? "Opens Claim your spot" : HasLatestMatch ? "Opens match stats" : "Opens the Stats tab";

    [ObservableProperty]
    private string _comingUpLabel = string.Empty;

    [ObservableProperty]
    private string _scheduleActionLabel = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<SessionSummaryDto> _comingUpSessions = [];

    [ObservableProperty]
    private bool _canManageSessions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotificationsSemanticDescription))]
    [NotifyPropertyChangedFor(nameof(HasUnreadAnnouncements))]
    [NotifyPropertyChangedFor(nameof(UnreadAnnouncementBadge))]
    private int _unreadAnnouncementCount;

    /// <summary>The count the API stops counting at; past it the badge reads "99+".</summary>
    private const int UnreadBadgeCap = 99;

    public bool HasUnreadAnnouncements => UnreadAnnouncementCount > 0;

    /// <summary>
    /// Badge text for the notification bell. The API caps its count, so rendering the raw number
    /// past the cap would understate reality — and a four-digit badge would not fit anyway.
    /// </summary>
    public string UnreadAnnouncementBadge =>
        UnreadAnnouncementCount >= UnreadBadgeCap ? $"{UnreadBadgeCap}+" : UnreadAnnouncementCount.ToString();

    public string NotificationsSemanticDescription => UnreadAnnouncementCount switch
    {
        0 => "Notifications, none unread",
        1 => "Notifications, 1 unread",
        _ => $"Notifications, {UnreadAnnouncementCount} unread",
    };

    partial void OnCanManageSessionsChanged(bool value) => CreateSessionCommand.NotifyCanExecuteChanged();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadDashboardAsync(forceProfileRefresh: false, cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Refresh(CancellationToken cancellationToken)
    {
        // Pulling to refresh is an explicit "I want current data" gesture, so it must not be
        // answered from the cache the way returning to the tab is. That includes the notification
        // bell: the badge is read through its own cache key, so invalidating only "sessions:" left
        // the unread count up to its TTL stale on the one gesture that means "check again".
        responseCache.Invalidate("sessions:");
        responseCache.Invalidate("announcements:");
        return LoadDashboardAsync(forceProfileRefresh: true, cancellationToken);
    }

    [RelayCommand]
    private Task ViewSessionDetail(Guid sessionId) => navigator.GoToSessionAsync(sessionId);

    [RelayCommand]
    private Task OpenStats() =>
        RequiresClaim
            // The player isn't linked to the game yet - send them to claim their spot first.
            ? navigator.GoToClaimSpotAsync(StatsPrompt!.SessionId)
            : HasLatestMatch
                // HasLatestMatch guarantees StatsPrompt is non-null with a real match id.
                ? navigator.GoToMatchStatsAsync(StatsPrompt!.MatchId)
                : navigator.GoToStatsAsync();

    [RelayCommand]
    private Task ViewSchedule() => navigator.GoToScheduleAsync();

    public const string ComingSoonTitle = "Coming soon";
    public const string ComingSoonMessage =
        "Announcements are still being built. We'll turn this on as soon as it's ready.";

    /// <summary>
    /// Both announcement entry points are held back: navigating to either page blocks the main
    /// thread long enough for iOS to terminate the app (watchdog 0x8BADF00D — a layout hang, not an
    /// exception, so there is nothing to catch). Nothing downstream is deleted — the page models,
    /// clients, Shell routes, and DI registrations all stay live — only these two commands are
    /// diverted, so restoring the feature means putting the two navigator calls back and nothing
    /// else. The original calls are kept below in comments for exactly that reason.
    /// </summary>
    [RelayCommand]
    private Task OpenAnnouncements() => ShowComingSoonAsync();
    // Restore with:
    //     announcementsNavigator is null || _primaryGroupId == Guid.Empty
    //         ? Task.CompletedTask
    //         : announcementsNavigator.GoToAnnouncementsAsync(_primaryGroupId);

    [RelayCommand]
    private Task OpenBroadcast() => ShowComingSoonAsync();
    // Restore with:
    //     announcementsNavigator?.GoToAdminBroadcastAsync() ?? Task.CompletedTask;

    private Task ShowComingSoonAsync() =>
        dialogService?.ShowAlertAsync(ComingSoonTitle, ComingSoonMessage, "Got it") ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanCreateSession))]
    private Task CreateSession() =>
        CanManageSessions ? navigator.GoToCreateSessionAsync() : Task.CompletedTask;

    private bool CanCreateSession() => CanManageSessions;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task JoinWaitlist(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await sessionsClient.JoinWaitlistAsync(sessionId, cancellationToken);
            if (!result.IsSuccess)
            {
                ApplyErrorState(
                    ViewState.Error,
                    "Couldn't join the waitlist",
                    result.ErrorMessage ?? ErrorMessage);
                return;
            }

            await LoadDashboardAsync(forceProfileRefresh: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            ApplyErrorState(ViewState.Offline, OfflineTitle, OfflineMessage);
        }
        catch (Exception)
        {
            // Includes HttpClient timeouts (TaskCanceledException whose token is NOT ours — the
            // guarded catch above lets them fall through here). Rethrowing them out of a
            // fire-and-forget command showed no UI at all, which read as a hang.
            ApplyErrorState(ViewState.Error, ErrorTitle, ErrorMessage);
        }
    }

    private async Task LoadDashboardAsync(bool forceProfileRefresh, CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        IsRefreshing = true;

        try
        {
            // The dashboard and badge are independent aggregate reads. Start them together so the
            // announcement badge does not add another serial network latency to Sessions startup.
            var dashboardTask = sessionsClient.GetDashboardAsync(cancellationToken);
            var unreadTask = GetUnreadCountOrCurrentAsync(cancellationToken);
            var dashboard = await dashboardTask;
            var profileContext = await BuildProfileContextOrDefaultAsync(
                dashboard.Greeting,
                forceProfileRefresh,
                cancellationToken);
            var groupLabel = await ResolveGroupLabelOrDefaultAsync(
                dashboard.GroupLabel,
                forceProfileRefresh,
                cancellationToken);
            UnreadAnnouncementCount = await unreadTask;

            ApplyDashboard(dashboard, profileContext.Greeting, groupLabel, profileContext.CanManageSessions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            ApplyErrorState(ViewState.Offline, OfflineTitle, OfflineMessage);
        }
        catch (Exception)
        {
            ApplyErrorState(ViewState.Error, ErrorTitle, ErrorMessage);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void ApplyDashboard(SessionsDashboardDto dashboard, string greeting, string groupLabel, bool canManageSessions)
    {
        GroupLabel = groupLabel;
        Greeting = greeting;
        DuesStatus = dashboard.DuesStatus;
        FeaturedSession = dashboard.FeaturedSession;
        StatsPrompt = ResolveStatsPrompt(dashboard.StatsPrompt);
        ComingUpLabel = dashboard.ComingUpLabel;
        ScheduleActionLabel = dashboard.ScheduleActionLabel;
        ComingUpSessions = dashboard.ComingUpSessions;
        CanManageSessions = canManageSessions;

        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

    // A claim prompt the player already answered "None of these are me" to has nothing for them to
    // submit, so suppress it. Only claim prompts are dismissable, so if they later get linked and the
    // server sends a real submit prompt (RequiresClaim = false) it still shows.
    private StatsPromptDto? ResolveStatsPrompt(StatsPromptDto? prompt) =>
        prompt is { RequiresClaim: true, SessionId: var sessionId } && dismissedPromptStore.IsDismissed(sessionId)
            ? null
            : prompt;

    private async Task<ProfileHomeContext> BuildProfileContextOrDefaultAsync(
        string fallbackGreeting,
        bool forceProfileRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceProfileRefresh && !string.IsNullOrWhiteSpace(_cachedProfileDisplayName))
        {
            return new ProfileHomeContext(
                BuildGreeting(_cachedProfileDisplayName),
                IsAdministrativeRole(_cachedProfileRole));
        }

        try
        {
            var profile = await profileClient.GetCurrentProfileAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(profile?.DisplayName))
            {
                return new ProfileHomeContext(
                    string.IsNullOrWhiteSpace(_cachedProfileDisplayName)
                        ? fallbackGreeting
                        : BuildGreeting(_cachedProfileDisplayName),
                    IsAdministrativeRole(_cachedProfileRole));
            }

            _cachedProfileDisplayName = profile.DisplayName;
            _cachedProfileRole = profile.Role;

            return new ProfileHomeContext(
                BuildGreeting(profile.DisplayName),
                IsAdministrativeRole(profile.Role));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new ProfileHomeContext(fallbackGreeting, IsAdministrativeRole(_cachedProfileRole));
        }
    }

    // The header shows the player's own group chat (their primary/default group, or the first one they
    // linked) instead of a hardcoded brand label. Falls back to the dashboard's default label when the
    // player hasn't linked a group yet. Cached like the profile name so we don't re-fetch on every
    // Appearing; a pull-to-refresh forces a reload.
    private async Task<string> ResolveGroupLabelOrDefaultAsync(
        string fallbackLabel,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh && !string.IsNullOrWhiteSpace(_cachedGroupLabel))
        {
            return _cachedGroupLabel;
        }

        try
        {
            var myGroups = await groupsClient.GetMyGroupsAsync(cancellationToken);
            var primary = myGroups.Groups.FirstOrDefault(group => group.IsPrimary)
                ?? myGroups.Groups.FirstOrDefault();

            if (primary is null || string.IsNullOrWhiteSpace(primary.GroupName))
            {
                return string.IsNullOrWhiteSpace(_cachedGroupLabel) ? fallbackLabel : _cachedGroupLabel;
            }

            _cachedGroupLabel = primary.GroupName;
            _primaryGroupId = primary.Id;
            return _cachedGroupLabel;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return string.IsNullOrWhiteSpace(_cachedGroupLabel) ? fallbackLabel : _cachedGroupLabel;
        }
    }

    private static bool IsAdministrativeRole(string? role) => PlayerRoles.IsAdministrative(role);

    private sealed record ProfileHomeContext(string Greeting, bool CanManageSessions);
    private string BuildGreeting(string displayName)
    {
        var firstName = displayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = displayName.Trim();
        }

        var hour = timeProvider.GetLocalNow().Hour;
        var dayPart = hour switch
        {
            < 12 => "morning",
            < 17 => "afternoon",
            _ => "evening"
        };

        return $"Good {dayPart}, {firstName}";
    }

    private void ApplyErrorState(ViewState state, string title, string message)
    {
        StateTitle = title;
        StateMessage = message;
        State = state;
    }

    private async Task<int> GetUnreadCountOrCurrentAsync(CancellationToken cancellationToken)
    {
        if (announcementsClient is null)
        {
            return UnreadAnnouncementCount;
        }

        try
        {
            return (await announcementsClient.GetUnreadCountAsync(cancellationToken)).UnreadCount;
        }
        catch
        {
            // The bell is supplementary. A badge outage must never hide an otherwise valid
            // Sessions dashboard, and containing cancellation here guarantees the independently
            // started task is always observed even if the dashboard load fails first.
            return UnreadAnnouncementCount;
        }
    }
}
