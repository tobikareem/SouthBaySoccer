using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

public interface IAdminMatchNavigator
{
    Task GoBackAsync();
}

/// <summary>
/// Game-admin matching: for one game, list the imported entries that never linked to a profile,
/// then attach each to a real player picked from the directory. The authoritative counterpart to
/// self-claim - an admin can link on anyone's behalf and re-link a wrong one, since the underlying
/// command overwrites.
/// </summary>
public partial class AdminMatchPageModel(
    IGameDayClient gameDayClient,
    IPlayersClient playersClient,
    IAdminMatchNavigator navigator) : ObservableObject
{
    public const string EmptyTitle = "Everyone's matched";
    public const string EmptyMessage = "Every imported player on this game is linked to a profile.";
    public const string ErrorTitle = "Couldn't load players";
    public const string ErrorMessage = "Something went wrong. Please try again.";

    private Guid sessionId;
    private IReadOnlyList<PlayerDirectoryEntryDto> _directory = [];

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUnlinked))]
    private IReadOnlyList<ClaimableParticipantDto> _unlinked = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUnlinked))]
    [NotifyPropertyChangedFor(nameof(ShowPicker))]
    [NotifyPropertyChangedFor(nameof(PickerPrompt))]
    private ClaimableParticipantDto? _selectedEntry;

    [ObservableProperty]
    private IReadOnlyList<PlayerDirectoryEntryDto> _candidates = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public bool ShowUnlinked => SelectedEntry is null && Unlinked.Count > 0;

    public bool ShowPicker => SelectedEntry is not null;

    public string PickerPrompt => SelectedEntry is { } entry
        ? $"Link \"{entry.DisplayName}\" to a player"
        : string.Empty;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Retry(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand]
    private Task Back() =>
        SelectedEntry is null ? navigator.GoBackAsync() : CancelPickAsync();

    [RelayCommand]
    private void SelectEntry(ClaimableParticipantDto? entry)
    {
        if (entry is null)
        {
            return;
        }

        SelectedEntry = entry;
        SearchText = string.Empty;
        ApplyFilter();
    }

    [RelayCommand]
    private Task CancelPick() => CancelPickAsync();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LinkTo(PlayerDirectoryEntryDto? candidate, CancellationToken cancellationToken)
    {
        if (candidate is null || SelectedEntry is not { } entry || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await gameDayClient.LinkParticipantAsync(
                entry.ParticipantId,
                candidate.Player.Id,
                cancellationToken);
            if (!result.IsSuccess)
            {
                ApplyNonContent(ViewState.Error, ErrorTitle, result.ErrorMessage ?? ErrorMessage);
                return;
            }

            // Drop the just-matched entry and return to the list; empty out when none remain.
            Unlinked = Unlinked.Where(x => x.ParticipantId != entry.ParticipantId).ToArray();
            SelectedEntry = null;
            if (Unlinked.Count == 0)
            {
                ApplyNonContent(ViewState.Empty, EmptyTitle, EmptyMessage);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            ApplyNonContent(ViewState.Offline, "You're offline", "Reconnect to match players.");
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

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private Task CancelPickAsync()
    {
        SelectedEntry = null;
        SearchText = string.Empty;
        return Task.CompletedTask;
    }

    private void ApplyFilter()
    {
        var term = SearchText?.Trim() ?? string.Empty;
        Candidates = term.Length == 0
            ? _directory
            : _directory
                .Where(entry => entry.Player.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        try
        {
            var unlinked = await gameDayClient.GetUnlinkedParticipantsAsync(sessionId, cancellationToken);
            if (unlinked.Count == 0)
            {
                ApplyNonContent(ViewState.Empty, EmptyTitle, EmptyMessage);
                return;
            }

            var directory = await playersClient.GetDirectoryAsync(cancellationToken);
            _directory = directory.Players;
            Unlinked = unlinked;
            SelectedEntry = null;
            ApplyFilter();
            StateTitle = string.Empty;
            StateMessage = string.Empty;
            State = ViewState.Content;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            ApplyNonContent(ViewState.Offline, "You're offline", "Reconnect to match players.");
        }
        catch (Exception)
        {
            ApplyNonContent(ViewState.Error, ErrorTitle, ErrorMessage);
        }
    }

    private void ApplyNonContent(ViewState state, string title, string message)
    {
        Unlinked = [];
        SelectedEntry = null;
        StateTitle = title;
        StateMessage = message;
        State = state;
    }

    /// <summary>Public so the shared test build can drive the session-scoped page.</summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("sessionId", out var value) && Guid.TryParse(value?.ToString(), out var parsed))
        {
            sessionId = parsed;
        }
    }
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
public partial class AdminMatchPageModel : IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query) => ApplyQueryAttributes(query);
}

public sealed class ShellAdminMatchNavigator : IAdminMatchNavigator
{
    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}
#endif
