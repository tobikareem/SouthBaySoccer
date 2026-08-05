using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Read-only view of a session's teams for any rostered player: each team with its members, the
/// player's own team marked, so they can see who they're with and who they're up against.
/// </summary>
public partial class TeamsViewPageModel(
    IGameDayClient gameDayClient,
    IGameDayNavigator navigator,
    IPollingDelay? pollingDelay = null,
    IAppLifecycleState? appLifecycleState = null) : ObservableObject
{
    public const string EmptyTitle = "No teams yet";
    public const string EmptyMessage = "Teams haven't been set for this game.";
    public const string ErrorTitle = "Couldn't load teams";
    public const string ErrorMessage = "Something went wrong. Please try again.";

    private Guid sessionId;
    private long draftRevision;
    private string? draftValidator;
    private CancellationTokenSource? pollingCancellation;
    private Task? pollingTask;
    private readonly IAppLifecycleState lifecycleState = appLifecycleState ?? AlwaysActiveAppLifecycleState.Instance;

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
    private async Task Appearing(CancellationToken cancellationToken)
    {
        await StopPollingAsync();
        var pageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pollingCancellation = pageCancellation;
        await LoadAsync(pageCancellation.Token);
        if (ReferenceEquals(pollingCancellation, pageCancellation)
            && !pageCancellation.IsCancellationRequested
            && State == ViewState.Content
            && IsDraftInProgress)
        {
            pollingTask = PollAsync(pageCancellation.Token);
        }
    }

    [RelayCommand]
    private Task Disappearing() => StopPollingAsync();

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
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            if (model is null || model.Teams.Count == 0)
            {
                ApplyNonContent(ViewState.Empty, EmptyTitle, EmptyMessage);
                return;
            }

            ApplyModel(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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

    private void ApplyModel(SessionTeamsDto model)
    {
        draftRevision = model.DraftRevision;
        draftValidator = model.DraftValidator;
        Teams = model.Teams;
        IsDraftInProgress = model.IsDraftInProgress;
        OnTheClockLabel = model.OnTheClockLabel;
        AvailablePlayers = model.AvailablePlayers ?? [];
        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        var pollImmediately = false;
        while (!cancellationToken.IsCancellationRequested && IsDraftInProgress)
        {
            var interval = failures switch
            {
                1 => TimeSpan.FromSeconds(5),
                2 => TimeSpan.FromSeconds(10),
                >= 3 => TimeSpan.FromSeconds(30),
                _ => TimeSpan.FromSeconds(5),
            };

            CancellationToken activeToken = default;
            try
            {
                activeToken = await lifecycleState.WaitForActiveTokenAsync(cancellationToken);
                using var activeRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, activeToken);
                if (!pollImmediately)
                {
                    await (pollingDelay ?? new JitteredPollingDelay()).DelayAsync(interval, activeRequest.Token);
                }
                pollImmediately = false;
                var requestedRevision = draftRevision;
                var result = string.IsNullOrWhiteSpace(draftValidator)
                    ? await gameDayClient.GetSessionTeamsIfChangedAsync(
                        sessionId,
                        requestedRevision,
                        activeRequest.Token)
                    : await gameDayClient.GetSessionTeamsIfChangedAsync(
                        sessionId,
                        requestedRevision,
                        draftValidator,
                        activeRequest.Token);
                failures = 0;
                var validatorChanged = result.Value is { } responseTeams
                    && !string.Equals(responseTeams.DraftValidator, draftValidator, StringComparison.Ordinal);
                if (result.Changed
                    && result.Value is { } changedTeams
                    && !activeRequest.IsCancellationRequested
                    && (changedTeams.DraftRevision > draftRevision || validatorChanged))
                {
                    ApplyModel(changedTeams);
                }
                else if (!result.Changed)
                {
                    draftValidator = result.Validator ?? draftValidator;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (activeToken.IsCancellationRequested)
            {
                pollImmediately = true;
            }
            catch (ApiRequestException exception) when (
                exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                break;
            }
            catch (HttpRequestException exception) when (
                exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                break;
            }
            catch (Exception)
            {
                failures++;
            }
        }
    }

    private async Task StopPollingAsync()
    {
        var cancellation = pollingCancellation;
        var task = pollingTask;
        cancellation?.Cancel();
        if (task is not null)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
        pollingCancellation = null;
        pollingTask = null;
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
