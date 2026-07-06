using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Services.Clients;
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
    TimeProvider timeProvider) : ObservableObject
{
    private string? _cachedProfileDisplayName;
    private string? _cachedProfileRole;

    public const string EmptyTitle = "No upcoming sessions";
    public const string EmptyMessage = "New game days will appear here as soon as they are scheduled.";
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
    private StatsPromptDto? _statsPrompt;

    [ObservableProperty]
    private string _comingUpLabel = string.Empty;

    [ObservableProperty]
    private string _scheduleActionLabel = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<SessionSummaryDto> _comingUpSessions = [];

    [ObservableProperty]
    private bool _canManageSessions;

    partial void OnCanManageSessionsChanged(bool value) => CreateSessionCommand.NotifyCanExecuteChanged();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadDashboardAsync(forceProfileRefresh: false, cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Refresh(CancellationToken cancellationToken) => LoadDashboardAsync(forceProfileRefresh: true, cancellationToken);

    [RelayCommand]
    private Task ViewSessionDetail(Guid sessionId) => navigator.GoToSessionAsync(sessionId);

    [RelayCommand]
    private Task OpenMatchStats(Guid matchId) => navigator.GoToMatchStatsAsync(matchId);

    [RelayCommand]
    private Task ViewSchedule() => navigator.GoToScheduleAsync();

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
                return;
            }

            await LoadDashboardAsync(forceProfileRefresh: false, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Refresh failed after a successful join; surface the standard error state
            // rather than throwing out of the command.
            ApplyErrorState(ViewState.Error, ErrorTitle, ErrorMessage);
        }
    }

    private async Task LoadDashboardAsync(bool forceProfileRefresh, CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        IsRefreshing = true;

        try
        {
            var dashboard = await sessionsClient.GetDashboardAsync(cancellationToken);
            var profileContext = await BuildProfileContextOrDefaultAsync(
                dashboard.Greeting,
                forceProfileRefresh,
                cancellationToken);

            ApplyDashboard(dashboard, profileContext.Greeting, profileContext.CanManageSessions);
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

    private void ApplyDashboard(SessionsDashboardDto dashboard, string greeting, bool canManageSessions)
    {
        GroupLabel = dashboard.GroupLabel;
        Greeting = greeting;
        DuesStatus = dashboard.DuesStatus;
        FeaturedSession = dashboard.FeaturedSession;
        StatsPrompt = dashboard.StatsPrompt;
        ComingUpLabel = dashboard.ComingUpLabel;
        ScheduleActionLabel = dashboard.ScheduleActionLabel;
        ComingUpSessions = dashboard.ComingUpSessions;
        CanManageSessions = canManageSessions;

        if (dashboard.FeaturedSession is null && dashboard.ComingUpSessions.Count == 0)
        {
            ApplyErrorState(ViewState.Empty, EmptyTitle, EmptyMessage);
            return;
        }

        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

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

    private static bool IsAdministrativeRole(string? role) =>
        role is not null &&
        (role.Equals("Owner", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("GameAdmin", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("Game Admin", StringComparison.OrdinalIgnoreCase));

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
}
