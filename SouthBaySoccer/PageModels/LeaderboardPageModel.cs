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
    IGroupsClient groupsClient,
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
    [NotifyPropertyChangedFor(nameof(HasGroups))]
    private IReadOnlyList<LeaderboardGroupOption> _groups = [];

    [ObservableProperty]
    private LeaderboardGroupOption? _selectedGroup;

    /// <summary>True once the group filter has options to show (linked groups plus "All").</summary>
    public bool HasGroups => Groups.Count > 1;

    // Guards the initial group population so setting SelectedGroup for the first time does not
    // trigger a redundant reload before the very first ranking load runs.
    private bool _suppressGroupReload;
    private bool _groupsLoaded;

    [ObservableProperty]
    private IReadOnlyList<LeaderboardRowItem> _rankings = [];

    /// <summary>
    /// True when the season has no rankings for the selected metric. Rendered as an inline
    /// placeholder under the metric tabs (per the wireframe) rather than a full-screen empty
    /// state, so the Goals/Assists/Rating/MVP tabs stay visible and switchable.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRankings))]
    private bool _isEmpty;

    /// <summary>True when there is at least one ranking row to render.</summary>
    public bool HasRankings => !IsEmpty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string _note = string.Empty;

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectMetric))]
    [NotifyPropertyChangedFor(nameof(CanSelectGroup))]
    [NotifyCanExecuteChangedFor(nameof(SelectMetricCommand))]
    private bool _isBusy;

    public bool CanSelectMetric => !IsBusy;

    public bool CanSelectGroup => !IsBusy;

    partial void OnSelectedMetricIndexChanged(int value)
    {
        if (value >= 0 && value < Metrics.Count)
        {
            SelectedMetric = Metrics[value].Metric;
        }
    }

    partial void OnSelectedGroupChanged(LeaderboardGroupOption? value)
    {
        // Reload when the player picks a different group, but not while we are seeding the list or a
        // load is already running.
        if (_suppressGroupReload || IsBusy)
        {
            return;
        }

        _ = LoadRankingAsync(CancellationToken.None);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Appearing(CancellationToken cancellationToken)
    {
        await LoadGroupsAsync(cancellationToken);
        await LoadRankingAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Refresh(CancellationToken cancellationToken) => LoadRankingAsync(cancellationToken);

    // Loads the player's linked groups once and seeds the filter with an "All" aggregate plus each
    // group, defaulting to the player's primary group. A failure here is non-fatal — the leaderboard
    // still loads on the aggregate view.
    private async Task LoadGroupsAsync(CancellationToken cancellationToken)
    {
        if (_groupsLoaded)
        {
            return;
        }

        try
        {
            var myGroups = await groupsClient.GetMyGroupsAsync(cancellationToken);
            var groupOptions = new List<LeaderboardGroupOption> { LeaderboardGroupOption.All };
            groupOptions.AddRange(myGroups.Groups.Select(group =>
                new LeaderboardGroupOption(group.Id, group.GroupName, group.IsPrimary)));

            _suppressGroupReload = true;
            Groups = groupOptions;
            SelectedGroup = groupOptions.FirstOrDefault(option => option.IsPrimary) ?? LeaderboardGroupOption.All;
            _suppressGroupReload = false;
            _groupsLoaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Leave the filter empty (hidden) and fall back to the aggregate leaderboard.
            _suppressGroupReload = false;
        }
    }

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
                SelectedGroup?.Id,
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
        // The unknown-season client fallback carries a blank label; keep the last known one so
        // the badge never renders empty.
        if (!string.IsNullOrWhiteSpace(ranking.SeasonLabel))
        {
            Season = ranking.SeasonLabel;
        }
        SelectedMetric = ranking.Metric;
        // Top 5 only, even if the server returns more.
        Rankings = ranking.Rows
            .Take(5)
            .Select(row => LeaderboardRowItem.From(row, ranking.Metric))
            .ToArray();
        Note = ranking.Note;
        IsEmpty = Rankings.Count == 0;

        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

    private void ApplyNonContentState(ViewState state, string title, string message)
    {
        Rankings = [];
        Note = string.Empty;
        IsEmpty = false;
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

/// <summary>
/// One option in the leaderboard group filter. A null <see cref="Id"/> is the "All" aggregate across
/// every group; otherwise it is the internal group-chat id passed to the leaderboard query.
/// </summary>
public sealed record LeaderboardGroupOption(Guid? Id, string Name, bool IsPrimary = false)
{
    public static LeaderboardGroupOption All { get; } = new(null, "All groups");
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
            BuildDetail(row),
            row.Rank == 1 ? Fonts.FontAwesomeGlyphs.Trophy : row.Rank.ToString(CultureInfo.InvariantCulture),
            row.Rank == 1 ? "FontAwesomeSolid" : "InterSemibold",
            row.Rank == 1 ? "Leader" : $"Rank {row.Rank.ToString(CultureInfo.InvariantCulture)}",
            FormatValue(row.Value, metric),
            row.Rank == 1);

    // Detail reads "{field position} · {n} apps", dropping the position when absent (imported players
    // often carry none). The rank is already shown as the numbered leading indicator, so it is not
    // repeated here as an ordinal. The separator is an explicit · so a file-encoding round-trip can
    // never mangle it into "Â·".
    private static string BuildDetail(LeaderboardRowDto row)
    {
        var appearances = row.Appearances;
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(row.Player.Position))
        {
            parts.Add(row.Player.Position.Trim());
        }

        parts.Add(appearances == 1
            ? "1 app"
            : $"{appearances.ToString(CultureInfo.InvariantCulture)} apps");
        return string.Join(" · ", parts);
    }

    private static string FormatValue(decimal value, LeaderboardMetric metric) =>
        metric == LeaderboardMetric.Rating
            ? value.ToString("0.0", CultureInfo.InvariantCulture)
            : value.ToString("0", CultureInfo.InvariantCulture);
}

