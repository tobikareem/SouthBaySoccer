using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Game-admin follow-up for games that have already been played: pick a past game, then jump into
/// the same captain, team, and stat-approval screens used on the day itself.
/// </summary>
public partial class RecentGamesPageModel(
    IGameDayClient gameDayClient,
    IGameDayNavigator navigator) : ObservableObject
{
    public const string EmptyTitle = "No recent games";
    public const string EmptyMessage = "Games you can still edit will appear here for a few days after kick-off.";
    public const string ErrorTitle = "Couldn't load recent games";
    public const string ErrorMessage = "Something went wrong loading games you played recently.";

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<RecentGameItem> _games = [];

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Retry(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    [RelayCommand]
    private Task OpenCaptains(RecentGameItem? game) =>
        game is null ? Task.CompletedTask : navigator.OpenCaptainAssignmentAsync(game.SessionId);

    [RelayCommand]
    private Task OpenTeams(RecentGameItem? game) =>
        game is null ? Task.CompletedTask : navigator.OpenTeamDraftAsync(game.SessionId);

    [RelayCommand]
    private Task OpenStats(RecentGameItem? game) =>
        game is null ? Task.CompletedTask : navigator.OpenPostGameApprovalAsync(game.SessionId);

    [RelayCommand]
    private Task OpenMatch(RecentGameItem? game) =>
        game is null ? Task.CompletedTask : navigator.OpenAdminMatchAsync(game.SessionId);

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        try
        {
            var games = await gameDayClient.GetRecentGamesAsync(cancellationToken);
            Games = games.Select(RecentGameItem.From).ToArray();
            if (Games.Count == 0)
            {
                ApplyNonContent(ViewState.Empty, EmptyTitle, EmptyMessage);
                return;
            }

            StateTitle = string.Empty;
            StateMessage = string.Empty;
            State = ViewState.Content;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            ApplyNonContent(ViewState.Offline, "You're offline", "Reconnect to load recent games.");
        }
        catch (Exception)
        {
            ApplyNonContent(ViewState.Error, ErrorTitle, ErrorMessage);
        }
    }

    private void ApplyNonContent(ViewState state, string title, string message)
    {
        Games = [];
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}

public sealed record RecentGameItem(
    Guid SessionId,
    Guid MatchId,
    string Title,
    string Detail,
    string StatusLabel,
    string PendingLabel,
    bool HasPending,
    bool CanEditTeams)
{
    public static RecentGameItem From(RecentGameDto game) =>
        new(
            game.SessionId,
            game.MatchId,
            game.Title,
            $"{game.DateLabel} - {game.Venue}",
            DescribeStatus(game),
            game.PendingApprovalCount == 1
                ? "1 stat awaiting review"
                : $"{game.PendingApprovalCount} stats awaiting review",
            game.PendingApprovalCount > 0,
            game.CanEditTeams);

    private static string DescribeStatus(RecentGameDto game) => game.StatusLabel switch
    {
        "NotStarted" => "No teams yet",
        "Draft" => $"{game.TeamCount} teams - draft",
        "InProgress" => $"{game.TeamCount} teams - locked",
        "Completed" => "Results recorded",
        "NeedsReview" => "Needs review",
        "Published" => "Published",
        "Locked" => "Locked",
        _ => game.StatusLabel,
    };
}
