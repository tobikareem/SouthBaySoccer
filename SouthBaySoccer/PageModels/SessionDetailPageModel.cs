using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Rosters;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Page model for the Session Detail screen (RSVP-8). Loads a single session and its roster through
/// <see cref="ISessionsClient"/> and <see cref="IRosterClient"/>, projects them onto the wireframe's
/// when/where, capacity, going and ordered-waitlist read models, and toggles the viewer's RSVP intent
/// optimistically. Maps every failure onto a <c>StateView</c> view state.
/// </summary>
/// <remarks>
/// This file is intentionally free of MAUI types so the behaviour is unit-testable in the plain client
/// test project. The <see cref="IQueryAttributable"/> bridge, Shell back-navigation, and map launch
/// live in the <c>SessionDetailPageModel.Navigation.cs</c> partial, compiled only into the MAUI app.
/// </remarks>
public partial class SessionDetailPageModel(
    ISessionsClient sessionsClient,
    IRosterClient rosterClient) : ObservableObject
{
    /// <summary>Shell query key carrying the session id (Guid string).</summary>
    public const string SessionIdQueryKey = "sessionId";

    /// <summary>
    /// Number of going players shown inline before the roster collapses behind a
    /// "+ N more going" affordance, matching the <c>session</c> wireframe.
    /// </summary>
    public const int GoingPreviewLimit = 4;

    public const string EmptyTitle = "Session not found";
    public const string EmptyMessage = "This pickup game is no longer available.";
    public const string ErrorTitle = "Couldn't load this session";
    public const string ErrorMessage = "Something went wrong loading the session. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load this session's roster and waitlist.";

    private Guid _sessionId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRsvp))]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private string _eyebrow = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMap))]
    private string _venue = string.Empty;

    [ObservableProperty]
    private string _dateTimeLabel = string.Empty;

    [ObservableProperty]
    private string _locationLabel = string.Empty;

    [ObservableProperty]
    private int _goingCount;

    [ObservableProperty]
    private int _capacity;

    [ObservableProperty]
    private string _deadlineLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GoingHeading))]
    [NotifyPropertyChangedFor(nameof(GoingPreview))]
    [NotifyPropertyChangedFor(nameof(MoreGoingCount))]
    [NotifyPropertyChangedFor(nameof(HasMoreGoing))]
    [NotifyPropertyChangedFor(nameof(MoreGoingLabel))]
    private IReadOnlyList<GoingRow> _going = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WaitlistHeading))]
    [NotifyPropertyChangedFor(nameof(HasWaitlist))]
    private IReadOnlyList<WaitlistRow> _waitlist = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RsvpButtonText))]
    private bool _isGoing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRsvp))]
    private bool _rsvpAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRsvp))]
    private bool _isCanceled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRsvp))]
    private bool _isUpdatingRsvp;

    /// <summary>True when a venue is known and a map link can be offered.</summary>
    public bool HasMap => !string.IsNullOrWhiteSpace(Venue);

    /// <summary>True when the RSVP toggle may be invoked (content loaded, RSVP open, no update in flight).</summary>
    public bool CanRsvp => State == ViewState.Content && RsvpAvailable && !IsCanceled && !IsUpdatingRsvp;

    /// <summary>Label for the primary RSVP button, reflecting the current intent.</summary>
    public string RsvpButtonText => IsGoing ? "Going — tap to withdraw" : "RSVP — I'm going";

    /// <summary>Going section heading with count, e.g. "Going · 16".</summary>
    public string GoingHeading => $"Going · {Going.Count}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GoingPreview))]
    [NotifyPropertyChangedFor(nameof(HasMoreGoing))]
    private bool _isGoingExpanded;

    /// <summary>
    /// The first <see cref="GoingPreviewLimit"/> going players shown inline; the remainder collapse
    /// behind <see cref="MoreGoingLabel"/> until it is tapped. Returns the full list when expanded
    /// or when it fits within the limit.
    /// </summary>
    public IReadOnlyList<GoingRow> GoingPreview =>
        !IsGoingExpanded && Going.Count > GoingPreviewLimit ? [.. Going.Take(GoingPreviewLimit)] : Going;

    /// <summary>Count of going players hidden behind the "+ N more going" affordance.</summary>
    public int MoreGoingCount => Math.Max(0, Going.Count - GoingPreviewLimit);

    /// <summary>True when collapsed going players exist and the expand affordance should show.</summary>
    public bool HasMoreGoing => !IsGoingExpanded && MoreGoingCount > 0;

    /// <summary>Collapsed-roster affordance label, e.g. "+ 12 more going".</summary>
    public string MoreGoingLabel => $"+ {MoreGoingCount} more going";

    /// <summary>Waitlist section heading with count, e.g. "Waitlist · 3".</summary>
    public string WaitlistHeading => $"Waitlist · {Waitlist.Count}";

    /// <summary>True when there is at least one waitlisted player.</summary>
    public bool HasWaitlist => Waitlist.Count > 0;

    /// <summary>
    /// Captures the <c>sessionId</c> Shell query parameter. Kept MAUI-free so it can be exercised
    /// directly in tests; the navigation partial forwards <see cref="IQueryAttributable"/> to it.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(SessionIdQueryKey, out var value) &&
            Guid.TryParse(value?.ToString(), out var sessionId))
        {
            _sessionId = sessionId;
        }
    }

    /// <summary>
    /// Loads the session detail and roster, mapping any failure onto the matching view state. Never
    /// throws out of the command.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Load(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;

        try
        {
            var session = await sessionsClient.GetSessionAsync(_sessionId, cancellationToken);
            if (session is null)
            {
                ResetContent();
                ApplyState(ViewState.Empty, EmptyTitle, EmptyMessage);
                return;
            }

            var roster = await rosterClient.GetRosterAsync(_sessionId, cancellationToken);

            ApplySession(session);
            ApplyRoster(roster);

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
            ApplyState(ViewState.Offline, OfflineTitle, OfflineMessage);
        }
        catch (Exception)
        {
            ApplyState(ViewState.Error, ErrorTitle, ErrorMessage);
        }
    }

    /// <summary>
    /// Optimistically toggles the viewer's going state, records the intent, and reverts on a failed
    /// result or exception. Refreshes the roster after a successful change. Never throws.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ToggleRsvp(CancellationToken cancellationToken)
    {
        if (!CanRsvp)
        {
            return;
        }

        var previousIsGoing = IsGoing;
        var desiredIsGoing = !previousIsGoing;

        IsUpdatingRsvp = true;
        IsGoing = desiredIsGoing;

        try
        {
            var result = await rosterClient.SetRsvpIntentAsync(
                _sessionId, desiredIsGoing, cancellationToken);

            if (!result.IsSuccess)
            {
                IsGoing = previousIsGoing;
                return;
            }

            await RefreshRosterAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            IsGoing = previousIsGoing;
            throw;
        }
        catch (Exception)
        {
            IsGoing = previousIsGoing;
        }
        finally
        {
            IsUpdatingRsvp = false;
        }
    }

    /// <summary>Expands the collapsed going roster to show every confirmed player.</summary>
    [RelayCommand]
    private void ShowAllGoing() => IsGoingExpanded = true;

    private async Task RefreshRosterAsync(CancellationToken cancellationToken)
    {
        var roster = await rosterClient.GetRosterAsync(_sessionId, cancellationToken);
        ApplyRoster(roster);
    }

    // The session DTO's availability covers the RSVP window (deadline, cancellation); capacity is
    // re-evaluated from the roster because the composed detail has no reliable going count.
    private bool _rsvpWindowOpen;

    private void ApplySession(SessionDetailDto session)
    {
        Eyebrow = session.Eyebrow;
        Venue = session.Venue;
        DateTimeLabel = session.DateTimeLabel;
        LocationLabel = session.LocationLabel;
        GoingCount = session.GoingCount;
        Capacity = session.Capacity;
        DeadlineLabel = session.DeadlineLabel;
        IsGoing = session.IsGoing;
        _rsvpWindowOpen = session.IsRsvpAvailable;
        RsvpAvailable = session.IsRsvpAvailable;
        IsCanceled = session.IsCanceled;
    }

    private void ApplyRoster(RosterDto? roster)
    {
        if (roster is null)
        {
            ResetContent();
            return;
        }

        Going = [.. roster.Going.Select(entry => new GoingRow(
            entry.Player.Initials,
            entry.Player.DisplayName,
            entry.Player.Position,
            entry.IsCurrentPlayer))];

        Waitlist = [.. roster.Waitlist.Select(entry => new WaitlistRow(
            entry.Position,
            entry.Position.ToString(),
            entry.Player.DisplayName,
            entry.Player.IsGuest,
            entry.Position == 1))];

        // Keep the capacity card and RSVP gate in sync with the authoritative roster.
        GoingCount = roster.Going.Count;
        RsvpAvailable = _rsvpWindowOpen && (Capacity <= 0 || roster.Going.Count < Capacity);
    }

    private void ResetContent()
    {
        Going = [];
        Waitlist = [];
        IsGoingExpanded = false;
    }

    private void ApplyState(ViewState state, string title, string message)
    {
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}

/// <summary>A player on the confirmed "Going" roster, projected for display.</summary>
public sealed record GoingRow(string Initials, string Name, string Position, bool IsViewer);

/// <summary>A player on the ordered waitlist, projected for display.</summary>
public sealed record WaitlistRow(int Position, string PositionLabel, string Name, bool IsGuest, bool IsNextUp);
