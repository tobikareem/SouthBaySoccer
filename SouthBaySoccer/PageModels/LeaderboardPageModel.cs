using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Leaderboards;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Loads and presents the season leaderboard behind the Stats Shell tab.
/// </summary>
public partial class LeaderboardPageModel(
    ILeaderboardClient leaderboardClient,
    ILeaderboardNavigator navigator,
    LeaderboardOptions options) : ObservableObject
{
    public const string EmptyTitle = "No rankings yet";
    public const string EmptyMessage = "Confirmed stats will appear here after match results are reviewed.";
    public const string ErrorTitle = "Couldn't load the leaderboard";
    public const string ErrorMessage = "Something went wrong loading the rankings. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load the latest rankings.";

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private string _season = options.SeasonLabel;

    [ObservableProperty]
    private IReadOnlyList<LeaderboardMetricOption> _metrics = LeaderboardMetricOption.All;

    [ObservableProperty]
    private int _selectedMetricIndex;

    [ObservableProperty]
    private LeaderboardMetric _selectedMetric = LeaderboardMetric.Goals;

    [ObservableProperty]
    private IReadOnlyList<LeaderboardRowItem> _rankings = [];

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectMetric))]
    [NotifyCanExecuteChangedFor(nameof(SelectMetricCommand))]
    private bool _isBusy;

    public bool CanSelectMetric => !IsBusy;

    partial void OnSelectedMetricIndexChanged(int value)
    {
        if (value >= 0 && value < Metrics.Count)
        {
            SelectedMetric = Metrics[value].Metric;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadRankingAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Refresh(CancellationToken cancellationToken) => LoadRankingAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanSelectMetric))]
    private Task SelectMetric(LeaderboardMetricOption? metric, CancellationToken cancellationToken)
    {
        if (metric is not null)
        {
            SelectedMetric = metric.Metric;
        }

        return LoadRankingAsync(cancellationToken);
    }

    [RelayCommand]
    private Task OpenPlayer(Guid playerId) => navigator.OpenPlayerProfileAsync(playerId);

    private async Task LoadRankingAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        IsBusy = true;

        try
        {
            var ranking = await leaderboardClient.GetRankingAsync(
                options.SeasonId,
                SelectedMetric,
                cancellationToken);

            ApplyRanking(ranking);
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

    private void ApplyRanking(LeaderboardDto ranking)
    {
        Season = ranking.SeasonLabel;
        SelectedMetric = ranking.Metric;
        Rankings = ranking.Rows.Select(row => LeaderboardRowItem.From(row, ranking.Metric)).ToArray();
        Note = ranking.Note;

        if (Rankings.Count == 0)
        {
            ApplyNonContentState(ViewState.Empty, EmptyTitle, EmptyMessage);
            return;
        }

        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

    private void ApplyNonContentState(ViewState state, string title, string message)
    {
        Rankings = [];
        Note = string.Empty;
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}

public sealed class LeaderboardOptions
{
    public Guid SeasonId { get; init; } = Guid.Parse("40000000-0000-0000-0000-000000000001");

    public string SeasonLabel { get; init; } = "Season 2026";
}

public sealed record LeaderboardMetricOption(LeaderboardMetric Metric, string Label)
{
    public static IReadOnlyList<LeaderboardMetricOption> All { get; } =
    [
        new(LeaderboardMetric.Goals, "Goals"),
        new(LeaderboardMetric.Assists, "Assists"),
        new(LeaderboardMetric.Rating, "Rating"),
        new(LeaderboardMetric.Mvp, "MVP")
    ];
}

public sealed record LeaderboardRowItem(
    int Rank,
    Guid PlayerId,
    string Initials,
    string Name,
    string Detail,
    string RankIndicator,
    string RankFontFamily,
    string RankDescription,
    string Value,
    bool IsLeader)
{
    public static LeaderboardRowItem From(LeaderboardRowDto row, LeaderboardMetric metric) =>
        new(
            row.Rank,
            row.Player.Id,
            row.Player.Initials,
            row.Player.DisplayName,
            $"{row.Player.Position} Â· {row.Appearances.ToString(CultureInfo.InvariantCulture)} apps",
            row.Rank == 1 ? Fonts.FontAwesomeGlyphs.Trophy : row.Rank.ToString(CultureInfo.InvariantCulture),
            row.Rank == 1 ? "FontAwesomeSolid" : "InterSemibold",
            row.Rank == 1 ? "Leader" : $"Rank {row.Rank.ToString(CultureInfo.InvariantCulture)}",
            FormatValue(row.Value, metric),
            row.Rank == 1);

    private static string FormatValue(decimal value, LeaderboardMetric metric) =>
        metric == LeaderboardMetric.Rating
            ? value.ToString("0.0", CultureInfo.InvariantCulture)
            : value.ToString("0", CultureInfo.InvariantCulture);
}

