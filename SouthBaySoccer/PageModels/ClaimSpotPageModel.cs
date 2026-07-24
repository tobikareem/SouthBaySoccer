using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

public interface IClaimSpotNavigator
{
    Task OpenSessionClaimAsync(Guid sessionId);

    Task GoBackAsync();
}

/// <summary>
/// Self-claim. Two modes: with no session it lists the recent games the signed-in player is not on
/// but could still claim a spot in (onboarding); with a session it lists that game's unclaimed
/// entries so the player can tap their own - shown next to their registered name for context.
/// </summary>
public partial class ClaimSpotPageModel(
    IGameDayClient gameDayClient,
    IClaimSpotNavigator navigator) : ObservableObject
{
    public const string EmptyTitle = "Nothing to claim";
    public const string EmptyMessage = "You're already on the roster for your recent games.";
    public const string ErrorTitle = "Couldn't load games";
    public const string ErrorMessage = "Something went wrong. Please try again.";

    private Guid? sessionId;

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private string _heading = "Claim your spot";

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSessions))]
    private IReadOnlyList<ClaimableSessionDto> _sessions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEntries))]
    private IReadOnlyList<ClaimableParticipantDto> _entries = [];

    [ObservableProperty]
    private bool _isBusy;

    public bool ShowSessions => sessionId is null && Sessions.Count > 0;

    public bool ShowEntries => sessionId is not null && Entries.Count > 0;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Retry(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    [RelayCommand]
    private Task OpenSession(ClaimableSessionDto? session) =>
        session is null ? Task.CompletedTask : navigator.OpenSessionClaimAsync(session.SessionId);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Claim(ClaimableParticipantDto? entry, CancellationToken cancellationToken)
    {
        if (entry is null || sessionId is not { } id || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await gameDayClient.ClaimParticipantAsync(id, entry.ParticipantId, cancellationToken);
            if (result.IsSuccess)
            {
                await navigator.GoBackAsync();
                return;
            }

            ApplyNonContent(ViewState.Error, ErrorTitle, result.ErrorMessage ?? ErrorMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            ApplyNonContent(ViewState.Offline, "You're offline", "Reconnect to claim your spot.");
        }
        catch (Exception)
        {
            ApplyNonContent(ViewState.Error, ErrorTitle, ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        try
        {
            if (sessionId is { } id)
            {
                var model = await gameDayClient.GetSessionClaimablesAsync(id, cancellationToken);
                if (model is null || model.AlreadyOnRoster || model.Claimable.Count == 0)
                {
                    ApplyNonContent(ViewState.Empty, "You're all set", "You're already on this game's roster.");
                    return;
                }

                Heading = "Which one is you?";
                Prompt = $"You signed in as {model.MyRegisteredName}. Tap the name you played under.";
                Entries = model.Claimable;
            }
            else
            {
                var sessions = await gameDayClient.GetMyClaimableSessionsAsync(cancellationToken);
                if (sessions.Count == 0)
                {
                    ApplyNonContent(ViewState.Empty, EmptyTitle, EmptyMessage);
                    return;
                }

                Heading = "Claim your spot";
                Prompt = "We couldn't match you automatically on these recent games. Tap one to find yourself.";
                Sessions = sessions;
            }

            StateTitle = string.Empty;
            StateMessage = string.Empty;
            State = ViewState.Content;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            ApplyNonContent(ViewState.Offline, "You're offline", "Reconnect to load your games.");
        }
        catch (Exception)
        {
            ApplyNonContent(ViewState.Error, ErrorTitle, ErrorMessage);
        }
    }

    private void ApplyNonContent(ViewState state, string title, string message)
    {
        Sessions = [];
        Entries = [];
        StateTitle = title;
        StateMessage = message;
        State = state;
    }

    /// <summary>
    /// Public so the shared (non-platform) test build can drive the session-scoped mode without the
    /// MAUI <c>IQueryAttributable</c> plumbing, which only compiles on platform targets.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        sessionId = query.TryGetValue("sessionId", out var value) && Guid.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : null;
    }
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
public partial class ClaimSpotPageModel : IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query) => ApplyQueryAttributes(query);
}

public sealed class ShellClaimSpotNavigator : IClaimSpotNavigator
{
    public Task OpenSessionClaimAsync(Guid sessionId) =>
        Shell.Current.GoToAsync($"claim-spot?sessionId={Uri.EscapeDataString(sessionId.ToString())}");

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}
#endif
