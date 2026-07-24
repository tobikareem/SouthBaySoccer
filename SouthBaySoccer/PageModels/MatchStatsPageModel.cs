using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Stats;
using ViewState = SouthBaySoccer.Controls.ViewState;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Loads a finished match's self-submitted stats and captain confirmation queue.
/// </summary>
public partial class MatchStatsPageModel(
    IStatsClient statsClient,
    IMatchStatsNavigator navigator,
    MatchStatsOptions options) : ObservableObject
{
    public const string HeaderTitle = "Match stats";
    public const string NoticeText = "Enter your final totals. A captain or game admin confirms every submission before it reaches the leaderboard.";
    public const string PerformanceSectionTitle = "Your performance";
    public const string PendingNote = "Sent to Pickup Pal · pending captain/admin";
    public const string ConfirmSectionTitle = "Confirm teammates · captain";
    public const string RateLinkText = "Rate teammates instead";
    public const string SubmitText = "Submit for confirmation";
    public const string PendingSubmitText = "Pending captain confirmation";
    public const string EmptyTitle = "No teammate submissions yet";
    public const string EmptyMessage = "Captain confirmations will appear here after teammates submit their totals.";
    public const string ErrorTitle = "Couldn't load match stats";
    public const string ErrorMessage = "Something went wrong loading this match. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load and submit match stats.";

    private Guid matchId = options.MatchId;
    private Guid currentPlayerId = options.CurrentPlayerId;

    private bool hasSubmittedStats;

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private string _matchSubtitle = options.MatchSubtitle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(SubmitButtonText))]
    [NotifyPropertyChangedFor(nameof(IsPendingNoteVisible))]
    [NotifyCanExecuteChangedFor(nameof(IncrementGoalsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DecrementGoalsCommand))]
    [NotifyCanExecuteChangedFor(nameof(IncrementAssistsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DecrementAssistsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private MatchStatsSubmitState _submitState = MatchStatsSubmitState.Editable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IncrementGoalsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DecrementGoalsCommand))]
    [NotifyCanExecuteChangedFor(nameof(IncrementAssistsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DecrementAssistsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private int _goals;

    [ObservableProperty]
    private int _assists;

    public ObservableCollection<TeammateSubmissionItem> TeammateSubmissions { get; } = [];

    public bool HasTeammateSubmissions => TeammateSubmissions.Count > 0;

    /// <summary>
    /// True only for a captain of this match or a game admin - the "Confirm teammates" section is
    /// their tool. A regular player never sees it, so it can't sit empty and unactionable.
    /// </summary>
    [ObservableProperty]
    private bool _canConfirmTeammates;

    public bool CanEdit => SubmitState == MatchStatsSubmitState.Editable && !IsBusy;

    public bool CanSubmit => SubmitState == MatchStatsSubmitState.Editable && !IsBusy;

    public string SubmitButtonText => SubmitState == MatchStatsSubmitState.Pending
        ? PendingSubmitText
        : SubmitText;

    public bool IsPendingNoteVisible => SubmitState == MatchStatsSubmitState.Pending;

    partial void OnGoalsChanged(int value)
    {
        if (value < 0)
        {
            Goals = 0;
        }
    }

    partial void OnAssistsChanged(int value)
    {
        if (value < 0)
        {
            Assists = 0;
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanSubmit));
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Retry(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void IncrementGoals() => Goals++;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void DecrementGoals()
    {
        if (Goals > 0)
        {
            Goals--;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void IncrementAssists() => Assists++;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void DecrementAssists()
    {
        if (Assists > 0)
        {
            Assists--;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanSubmit))]
    private async Task Submit(CancellationToken cancellationToken)
    {
        if (!CanSubmit || hasSubmittedStats)
        {
            return;
        }

        IsBusy = true;
        SubmitState = MatchStatsSubmitState.Submitting;

        try
        {
            var result = await statsClient.SubmitStatsAsync(matchId, Goals, Assists, cancellationToken);
            if (result.IsSuccess)
            {
                hasSubmittedStats = true;
                SubmitState = MatchStatsSubmitState.Pending;
                return;
            }

            SubmitState = MatchStatsSubmitState.Editable;
            ApplyNonContentState(ViewState.Error, ErrorTitle, result.ErrorMessage ?? ErrorMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SubmitState = MatchStatsSubmitState.Editable;
            throw;
        }
        catch (HttpRequestException)
        {
            SubmitState = MatchStatsSubmitState.Editable;
            ApplyNonContentState(ViewState.Offline, OfflineTitle, OfflineMessage);
        }
        catch (Exception)
        {
            SubmitState = MatchStatsSubmitState.Editable;
            ApplyNonContentState(ViewState.Error, ErrorTitle, ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ConfirmTeammate(TeammateSubmissionItem? teammate, CancellationToken cancellationToken)
    {
        if (teammate is null || teammate.IsConfirmed)
        {
            return;
        }

        var result = await statsClient.ConfirmStatsAsync(matchId, teammate.PlayerId, cancellationToken);
        if (result.IsSuccess)
        {
            teammate.IsConfirmed = true;
            return;
        }

        ApplyNonContentState(ViewState.Error, ErrorTitle, result.ErrorMessage ?? ErrorMessage);
    }

    [RelayCommand]
    private Task OpenRate() => navigator.OpenRateTeammatesAsync(matchId, currentPlayerId, MatchSubtitle);

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    public void ApplyMatchId(Guid value)
    {
        if (value != Guid.Empty)
        {
            matchId = value;
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        IsBusy = true;

        try
        {
            var stats = await statsClient.GetMatchStatsAsync(matchId, cancellationToken);
            if (stats is null)
            {
                ApplyNonContentState(ViewState.Empty, EmptyTitle, EmptyMessage);
                return;
            }

            ApplyStats(stats);
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

    private void ApplyStats(MatchStatsDto stats)
    {
        currentPlayerId = stats.CurrentPlayerId;
        MatchSubtitle = stats.MatchSubtitle;
        Goals = stats.Goals;
        Assists = stats.Assists;
        CanConfirmTeammates = stats.CanConfirmTeammates;
        SubmitState = stats.IsPendingConfirmation
            ? MatchStatsSubmitState.Pending
            : MatchStatsSubmitState.Editable;

        TeammateSubmissions.Clear();
        foreach (var teammate in stats.TeammateSubmissions.Select(TeammateSubmissionItem.From))
        {
            TeammateSubmissions.Add(teammate);
        }

        OnPropertyChanged(nameof(HasTeammateSubmissions));

        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

    private void ApplyNonContentState(ViewState state, string title, string message)
    {
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}

public enum MatchStatsSubmitState
{
    Editable,
    Submitting,
    Pending
}

public sealed class MatchStatsOptions
{
    public Guid MatchId { get; init; } = Guid.Parse("30000000-0000-0000-0000-000000000001");

    public Guid CurrentPlayerId { get; init; } = Guid.Parse("10000000-0000-0000-0000-000000000001");

    public string MatchSubtitle { get; init; } = "Sat \u00B7 Marina Field";
}

public interface IMatchStatsNavigator
{
    Task OpenRateTeammatesAsync(Guid matchId, Guid raterId, string matchSubtitle);

    Task GoBackAsync();
}

public partial class TeammateSubmissionItem(
    Guid playerId,
    string initials,
    string name,
    string detail,
    bool isConfirmed) : ObservableObject
{
    public Guid PlayerId { get; } = playerId;

    public string Initials { get; } = initials;

    public string Name { get; } = name;

    public string Detail { get; } = detail;

    [ObservableProperty]
    private bool _isConfirmed = isConfirmed;

    public static TeammateSubmissionItem From(TeammateStatSubmissionDto submission) =>
        new(
            submission.Player.Id,
            submission.Player.Initials,
            submission.Player.DisplayName,
            FormatDetail(submission),
            submission.IsConfirmed);

    private static string FormatDetail(TeammateStatSubmissionDto submission)
    {
        var parts = new List<string>();
        if (submission.Goals > 0)
        {
            parts.Add($"{submission.Goals} {(submission.Goals == 1 ? "goal" : "goals")}");
        }

        if (submission.Assists > 0)
        {
            parts.Add($"{submission.Assists} {(submission.Assists == 1 ? "assist" : "assists")}");
        }

        return parts.Count == 0
            ? "submitted: no goals or assists"
            : submission.IsConfirmed
                ? string.Join(" · ", parts)
                : $"submitted: {string.Join(" · ", parts)}";
    }
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
public sealed class ShellMatchStatsNavigator : IMatchStatsNavigator
{
    public Task OpenRateTeammatesAsync(Guid matchId, Guid raterId, string matchSubtitle) =>
        Shell.Current.GoToAsync($"rate-teammates?matchId={matchId}&raterId={raterId}&subtitle={Uri.EscapeDataString(matchSubtitle)}");

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}

public partial class MatchStatsPageModel : IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("matchId", out var value)
            && Guid.TryParse(value?.ToString(), out var matchId))
        {
            ApplyMatchId(matchId);
        }
    }
}
#endif



