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
    ISessionsNavigator navigator) : ObservableObject
{
    public const string EmptyTitle = "No upcoming sessions";
    public const string EmptyMessage = "New game days will appear here as soon as they are scheduled.";
    public const string ErrorTitle = "Couldn't load your sessions";
    public const string ErrorMessage = "Something went wrong loading your home screen. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load your dues status and upcoming sessions.";

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

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

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadDashboardAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Refresh(CancellationToken cancellationToken) => LoadDashboardAsync(cancellationToken);

    [RelayCommand]
    private Task ViewSessionDetail(Guid sessionId) => navigator.GoToSessionAsync(sessionId);

    [RelayCommand]
    private Task OpenMatchStats(Guid matchId) => navigator.GoToMatchStatsAsync(matchId);

    [RelayCommand]
    private Task ViewSchedule() => navigator.GoToScheduleAsync();

    [RelayCommand]
    private Task CreateSession() => navigator.GoToCreateSessionAsync();

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

            await LoadDashboardAsync(cancellationToken);
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

    private async Task LoadDashboardAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;

        try
        {
            var dashboard = await sessionsClient.GetDashboardAsync(cancellationToken);
            ApplyDashboard(dashboard);
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
    }

    private void ApplyDashboard(SessionsDashboardDto dashboard)
    {
        GroupLabel = dashboard.GroupLabel;
        Greeting = dashboard.Greeting;
        DuesStatus = dashboard.DuesStatus;
        FeaturedSession = dashboard.FeaturedSession;
        StatsPrompt = dashboard.StatsPrompt;
        ComingUpLabel = dashboard.ComingUpLabel;
        ScheduleActionLabel = dashboard.ScheduleActionLabel;
        ComingUpSessions = dashboard.ComingUpSessions;
        CanManageSessions = dashboard.CanManageSessions;

        if (dashboard.FeaturedSession is null && dashboard.ComingUpSessions.Count == 0)
        {
            ApplyErrorState(ViewState.Empty, EmptyTitle, EmptyMessage);
            return;
        }

        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

    private void ApplyErrorState(ViewState state, string title, string message)
    {
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}
