using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Read-only view of a session's teams for any rostered player: each team with its members, the
/// player's own team marked, so they can see who they're with and who they're up against.
/// </summary>
public partial class TeamsViewPageModel(
    IGameDayClient gameDayClient,
    IGameDayNavigator navigator) : ObservableObject
{
    public const string EmptyTitle = "No teams yet";
    public const string EmptyMessage = "Teams haven't been set for this game.";
    public const string ErrorTitle = "Couldn't load teams";
    public const string ErrorMessage = "Something went wrong. Please try again.";

    private Guid sessionId;

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<SessionTeamDto> _teams = [];

    /// <summary>True while captains are still picking: the view labels itself a live draft.</summary>
    [ObservableProperty]
    private bool _isDraftInProgress;

    [ObservableProperty]
    private string _onTheClockLabel = string.Empty;

    /// <summary>Going/Waitlist players not yet picked by any team, shown only mid-draft.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailablePlayers))]
    [NotifyPropertyChangedFor(nameof(AvailableHeader))]
    private IReadOnlyList<SessionTeamMemberDto> _availablePlayers = [];

    public bool HasAvailablePlayers => AvailablePlayers.Count > 0;

    public string AvailableHeader => $"Yet to be picked ({AvailablePlayers.Count})";

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Retry(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        try
        {
            var model = await gameDayClient.GetSessionTeamsAsync(sessionId, cancellationToken);
            if (model is null || model.Teams.Count == 0)
            {
                ApplyNonContent(ViewState.Empty, EmptyTitle, EmptyMessage);
                return;
            }

            Teams = model.Teams;
            IsDraftInProgress = model.IsDraftInProgress;
            OnTheClockLabel = model.OnTheClockLabel;
            AvailablePlayers = model.AvailablePlayers ?? [];
            StateTitle = string.Empty;
            StateMessage = string.Empty;
            State = ViewState.Content;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            ApplyNonContent(ViewState.Offline, "You're offline", "Reconnect to see the teams.");
        }
        catch (Exception)
        {
            ApplyNonContent(ViewState.Error, ErrorTitle, ErrorMessage);
        }
    }

    private void ApplyNonContent(ViewState state, string title, string message)
    {
        Teams = [];
        IsDraftInProgress = false;
        OnTheClockLabel = string.Empty;
        AvailablePlayers = [];
        StateTitle = title;
        StateMessage = message;
        State = state;
    }

    /// <summary>
    /// Public so the shared (non-platform) test build can drive the query-scoped mode without the
    /// MAUI <c>IQueryAttributable</c> plumbing, which only compiles on platform targets.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        sessionId = query.TryGetValue("sessionId", out var value) && Guid.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : Guid.Empty;
    }
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
public partial class TeamsViewPageModel : IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query) => ApplyQueryAttributes(query);
}
#endif
