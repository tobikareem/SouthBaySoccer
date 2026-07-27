using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// One titled group of sessions on the Schedule screen ("This week", "Next week", or a month).
/// </summary>
public sealed record ScheduleGroup(string Title, IReadOnlyList<SessionSummaryDto> Sessions);

/// <summary>
/// Page model for the upcoming-session Schedule screen. Loads every session the dashboard exposes
/// through <see cref="ISessionsClient"/>, orders them by start time, and groups them by
/// device-local week ("This week" / "Next week") then by month, mirroring the schedule wireframe.
/// Navigation is delegated to <see cref="ISessionsNavigator"/> so this model stays MAUI-free and
/// unit-testable.
/// </summary>
public partial class SchedulePageModel(
    ISessionsClient sessionsClient,
    ISessionsNavigator navigator,
    IClientResponseCache responseCache,
    TimeProvider timeProvider) : ObservableObject
{
    public const string ThisWeekTitle = "This week";
    public const string NextWeekTitle = "Next week";
    public const string EmptyTitle = "No upcoming sessions";
    public const string EmptyMessage = "New game days will appear here as soon as they are scheduled.";
    public const string ErrorTitle = "Couldn't load the schedule";
    public const string ErrorMessage = "Something went wrong loading the schedule. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load the upcoming session schedule.";

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ScheduleGroup> _groups = [];

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadScheduleAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Refresh(CancellationToken cancellationToken)
    {
        // See SessionsHomePageModel.Refresh: an explicit pull must bypass the shared dashboard cache.
        responseCache.Invalidate("sessions:");
        return LoadScheduleAsync(cancellationToken);
    }

    [RelayCommand]
    private Task ViewSessionDetail(Guid sessionId) => navigator.GoToSessionAsync(sessionId);

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

            await LoadScheduleAsync(cancellationToken);
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

    private async Task LoadScheduleAsync(CancellationToken cancellationToken)
    {
        // Keep the current content visible during a user-initiated pull-to-refresh (the RefreshView
        // spinner already signals progress); only show the Loading state on the first load or a retry.
        if (State != ViewState.Content)
        {
            State = ViewState.Loading;
        }

        IsRefreshing = true;

        try
        {
            var dashboard = await sessionsClient.GetDashboardAsync(cancellationToken);
            var sessions = EnumerateSessions(dashboard)
                .OrderBy(session => session.StartsAtUtc)
                .ToArray();

            if (sessions.Length == 0)
            {
                Groups = [];
                ApplyErrorState(ViewState.Empty, EmptyTitle, EmptyMessage);
                return;
            }

            Groups = BuildGroups(sessions);
            StateTitle = string.Empty;
            StateMessage = string.Empty;
            State = ViewState.Content;
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

    private static IEnumerable<SessionSummaryDto> EnumerateSessions(SessionsDashboardDto dashboard)
    {
        if (dashboard.FeaturedSession is not null)
        {
            yield return dashboard.FeaturedSession;
        }

        foreach (var session in dashboard.ComingUpSessions)
        {
            yield return session;
        }
    }

    private IReadOnlyList<ScheduleGroup> BuildGroups(IReadOnlyList<SessionSummaryDto> sessions)
    {
        var localToday = timeProvider.GetLocalNow().Date;
        // US convention: the week runs Sunday through Saturday, matching the group's game days.
        var thisWeekStart = localToday.AddDays(-(int)localToday.DayOfWeek);
        var nextWeekStart = thisWeekStart.AddDays(7);
        var laterStart = thisWeekStart.AddDays(14);

        return sessions
            .GroupBy(session => GroupTitleFor(ToLocalDate(session.StartsAtUtc), localToday, nextWeekStart, laterStart))
            .Select(group => new ScheduleGroup(group.Key, group.ToArray()))
            .ToArray();
    }

    private string GroupTitleFor(DateTime localDate, DateTime localToday, DateTime nextWeekStart, DateTime laterStart)
    {
        if (localDate < nextWeekStart)
        {
            return ThisWeekTitle;
        }

        if (localDate < laterStart)
        {
            return NextWeekTitle;
        }

        return localDate.Year == localToday.Year
            ? localDate.ToString("MMMM", CultureInfo.InvariantCulture)
            : localDate.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    }

    private DateTime ToLocalDate(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc.ToUniversalTime(),
            timeProvider.LocalTimeZone).Date;

    private void ApplyErrorState(ViewState state, string title, string message)
    {
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}
