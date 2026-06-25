using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Loads and filters the searchable player directory behind the Players Shell tab.
/// </summary>
public partial class PlayersPageModel(
    IPlayersClient playersClient,
    IPlayersNavigator navigator) : ObservableObject
{
    public const string EmptyTitle = "No players yet";
    public const string EmptyMessage = "The roster will appear here once players join Pickup Pal.";
    public const string NoMatchesTitle = "No matching players";
    public const string NoMatchesMessage = "Try searching by name, role, or position.";
    public const string ErrorTitle = "Couldn't load players";
    public const string ErrorMessage = "Something went wrong loading the directory. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load the player directory.";

    private IReadOnlyList<PlayerDirectoryEntryDto> allPlayers = [];

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private string _title = "Players";

    [ObservableProperty]
    private string _subtitle = "Search the crew and open career stats.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPlayersText))]
    private int _totalPlayers;

    public string TotalPlayersText => TotalPlayers.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<PlayerDirectoryRowItem> _players = [];

    [ObservableProperty]
    private bool _isBusy;

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadPlayersAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Refresh(CancellationToken cancellationToken) => LoadPlayersAsync(cancellationToken);

    [RelayCommand]
    private Task OpenPlayer(Guid playerId) => navigator.OpenPlayerProfileAsync(playerId);

    private async Task LoadPlayersAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        IsBusy = true;

        try
        {
            var directory = await playersClient.GetDirectoryAsync(cancellationToken);
            Title = directory.Title;
            Subtitle = directory.Subtitle;
            TotalPlayers = directory.TotalPlayers;
            allPlayers = directory.Players;
            ApplyFilter();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            ApplyNonContentState(ViewState.Offline, OfflineTitle, OfflineMessage);
        }
        catch (Exception)
        {
            ApplyNonContentState(ViewState.Error, ErrorTitle, ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var query = SearchQuery.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? allPlayers
            : allPlayers
                .Where(entry =>
                    entry.Player.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || entry.Player.Position.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || entry.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        Players = filtered.Select(PlayerDirectoryRowItem.From).ToArray();

        if (allPlayers.Count == 0)
        {
            ApplyNonContentState(ViewState.Empty, EmptyTitle, EmptyMessage);
            return;
        }

        if (Players.Count == 0)
        {
            StateTitle = NoMatchesTitle;
            StateMessage = NoMatchesMessage;
            State = ViewState.Empty;
            return;
        }

        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

    private void ApplyNonContentState(ViewState state, string title, string message)
    {
        Players = [];
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}

public sealed record PlayerDirectoryRowItem(
    Guid PlayerId,
    string Initials,
    string Name,
    string Detail,
    string TrailingText,
    bool IsGuest)
{
    public static PlayerDirectoryRowItem From(PlayerDirectoryEntryDto entry) =>
        new(
            entry.Player.Id,
            entry.Player.Initials,
            entry.Player.DisplayName,
            $"{entry.Subtitle} \u00B7 {entry.Matches} matches",
            entry.IsGuestLabel(),
            entry.Player.IsGuest);
}

internal static class PlayerDirectoryEntryExtensions
{
    public static string IsGuestLabel(this PlayerDirectoryEntryDto entry) =>
        entry.Player.IsGuest ? "guest" : string.Empty;
}



