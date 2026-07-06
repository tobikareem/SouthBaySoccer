using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

public sealed class GameDayOptions
{
    public DateTime VenueLocalNow { get; init; } = new(2026, 6, 20, 19, 35, 0);
}

public interface IGameDayNavigator
{
    Task OpenCaptainAssignmentAsync(Guid sessionId);

    Task OpenTeamDraftAsync(Guid sessionId);

    Task OpenPostGameApprovalAsync(Guid sessionId);

    Task GoBackAsync();
}

public partial class GameDayPageModel(
    IGameDayClient gameDayClient,
    IGameDayNavigator navigator,
    IProfileClient profileClient,
    GameDayOptions options) : ObservableObject
{
    public const string NoticeText = "RSVP is attendance intent. Game Day check-in records who is actually at the field.";
    public const string ErrorTitle = "Couldn't load Game Day";
    public const string ErrorMessage = "Something went wrong loading the active game-day flow.";

    private Guid sessionId;

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckInCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckInCommand))]
    private bool _canCheckIn;

    [ObservableProperty]
    private string _venue = string.Empty;

    [ObservableProperty]
    private string _statusLabel = string.Empty;

    [ObservableProperty]
    private string _gameStartLabel = string.Empty;

    [ObservableProperty]
    private string _checkInWindowLabel = string.Empty;

    [ObservableProperty]
    private string _checkInCloseLabel = string.Empty;

    [ObservableProperty]
    private string _primaryActionText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlockReason))]
    private string? _blockReason;

    public bool HasBlockReason => !string.IsNullOrWhiteSpace(BlockReason);

    [ObservableProperty]
    private int _goingCount;

    [ObservableProperty]
    private int _checkedInCount;

    [ObservableProperty]
    private int _lateCount;

    [ObservableProperty]
    private bool _isAdmin;

    public bool HasGameDayActions => CanAssignCaptains || CanDraftTeam || CanApprovePostGame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameDayActions))]
    [NotifyCanExecuteChangedFor(nameof(OpenCaptainAssignmentCommand))]
    private bool _canAssignCaptains;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameDayActions))]
    [NotifyCanExecuteChangedFor(nameof(OpenTeamDraftCommand))]
    private bool _canDraftTeam;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameDayActions))]
    [NotifyCanExecuteChangedFor(nameof(OpenPostGameApprovalCommand))]
    private bool _canApprovePostGame;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Retry(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanCheckInNow))]
    private async Task CheckIn(CancellationToken cancellationToken)
    {
        if (!CanCheckInNow())
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await gameDayClient.CheckInAsync(sessionId, Guid.NewGuid(), cancellationToken);
            if (result.IsSuccess)
            {
                await LoadAsync(cancellationToken);
                return;
            }

            ApplyNonContent(ViewState.Error, ErrorTitle, result.ErrorMessage ?? ErrorMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenCaptainAssignment))]
    private Task OpenCaptainAssignment() =>
        CanAssignCaptains ? navigator.OpenCaptainAssignmentAsync(sessionId) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanOpenTeamDraft))]
    private Task OpenTeamDraft() =>
        CanDraftTeam ? navigator.OpenTeamDraftAsync(sessionId) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanOpenPostGameApproval))]
    private Task OpenPostGameApproval() =>
        CanApprovePostGame ? navigator.OpenPostGameApprovalAsync(sessionId) : Task.CompletedTask;

    private bool CanCheckInNow() => CanCheckIn && !IsBusy;

    private bool CanOpenCaptainAssignment() => CanAssignCaptains;

    private bool CanOpenTeamDraft() => CanDraftTeam;

    private bool CanOpenPostGameApproval() => CanApprovePostGame;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        try
        {
            var context = await gameDayClient.GetTodayContextAsync(cancellationToken);
            if (context is null)
            {
                ApplyNonContent(ViewState.Empty, "No session today", "Your next eligible session will appear here on match day.");
                return;
            }

            var isAdmin = await LoadIsAdminAsync(cancellationToken);
            ApplyContext(ApplyWindow(context), isAdmin);
        }
        catch (HttpRequestException)
        {
            ApplyNonContent(ViewState.Offline, "You're offline", "Reconnect to check in at the field.");
        }
        catch (Exception)
        {
            ApplyNonContent(ViewState.Error, ErrorTitle, ErrorMessage);
        }
    }

    private GameDayContextDto ApplyWindow(GameDayContextDto context)
    {
        var time = options.VenueLocalNow.TimeOfDay;
        var isOpen = time >= new TimeSpan(19, 30, 0) && time <= new TimeSpan(19, 45, 0);
        if (isOpen || context.IsCurrentPlayerCheckedIn)
        {
            return context;
        }

        return context with
        {
            Status = GameDayStatus.Closed,
            StatusLabel = "Closed",
            IsSelfCheckInAvailable = false,
            PrimaryActionText = "GameAdmin override required",
            BlockReason = "Check-in closed at 7:45 PM. A GameAdmin override is required for late arrivals."
        };
    }

    private async Task<bool> LoadIsAdminAsync(CancellationToken cancellationToken)
    {
        try
        {
            var profile = await profileClient.GetCurrentProfileAsync(cancellationToken);
            return IsAdministrativeRole(profile?.Role);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsAdministrativeRole(string? role) =>
        role is not null &&
        (role.Equals("Owner", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("GameAdmin", StringComparison.OrdinalIgnoreCase) ||
         role.Equals("Game Admin", StringComparison.OrdinalIgnoreCase));

    private void ApplyContext(GameDayContextDto context, bool isAdmin)
    {
        sessionId = context.SessionId;
        IsAdmin = isAdmin;
        Venue = context.Venue;
        StatusLabel = context.StatusLabel;
        GameStartLabel = context.GameStartLabel;
        CheckInWindowLabel = context.CheckInWindowLabel;
        CheckInCloseLabel = context.CheckInCloseLabel;
        PrimaryActionText = context.PrimaryActionText;
        BlockReason = context.BlockReason;
        GoingCount = context.GoingCount;
        CheckedInCount = context.CheckedInCount;
        LateCount = context.LateCount;
        CanCheckIn = context.IsSelfCheckInAvailable;
        CanAssignCaptains = isAdmin || context.CanAssignCaptains;
        CanDraftTeam = isAdmin || context.CanDraftTeam;
        CanApprovePostGame = isAdmin || context.CanApprovePostGame;
        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

    private void ApplyNonContent(ViewState state, string title, string message)
    {
        State = state;
        StateTitle = title;
        StateMessage = message;
    }
}

public partial class CaptainAssignmentPageModel(
    IGameDayClient gameDayClient,
    IGameDayNavigator navigator) : ObservableObject
{
    private Guid sessionId;

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCountText))]
    private int _captainCount = 2;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public ObservableCollection<CaptainPlayerItem> Players { get; } = [];

    public string SelectedCountText => $"{Players.Count(item => item.IsSelected)} selected / max {CaptainCount}";

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand]
    private void SelectCaptainCount(object? count)
    {
        if (!TryParseCaptainCount(count, out var parsedCount))
        {
            return;
        }

        CaptainCount = parsedCount;
        foreach (var item in Players.Where(item => item.IsSelected).Skip(parsedCount))
        {
            item.IsSelected = false;
        }

        OnPropertyChanged(nameof(SelectedCountText));
    }

    private static bool TryParseCaptainCount(object? value, out int count)
    {
        count = value switch
        {
            int typedValue => typedValue,
            string text when int.TryParse(text, out var parsedValue) => parsedValue,
            _ => 0
        };

        return count is >= 2 and <= 4;
    }

    [RelayCommand]
    private void ToggleCaptain(CaptainPlayerItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!item.IsSelected && Players.Count(player => player.IsSelected) >= CaptainCount)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
        OnPropertyChanged(nameof(SelectedCountText));
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Grant(CancellationToken cancellationToken)
    {
        var selected = Players.Where(item => item.IsSelected).Select(item => item.PlayerId).ToArray();
        var result = await gameDayClient.AssignCaptainsAsync(sessionId, CaptainCount, selected, cancellationToken);
        if (result.IsSuccess)
        {
            await LoadAsync(cancellationToken);
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var dto = await gameDayClient.GetCaptainAssignmentAsync(sessionId == Guid.Empty ? Guid.Parse("20000000-0000-0000-0000-000000000001") : sessionId, cancellationToken);
        if (dto is null)
        {
            State = ViewState.Empty;
            return;
        }

        sessionId = dto.SessionId;
        CaptainCount = dto.CaptainCount;
        Players.Clear();
        foreach (var player in dto.CheckedInPlayers)
        {
            Players.Add(new CaptainPlayerItem(
                player.Player.Id,
                player.Player.Initials,
                player.Player.DisplayName,
                player.Detail,
                dto.SelectedCaptainIds.Contains(player.Player.Id)));
        }

        ApplyFilter();
        State = ViewState.Content;
    }

    private void ApplyFilter()
    {
        foreach (var item in Players)
        {
            item.IsVisible = string.IsNullOrWhiteSpace(SearchText)
                || item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public partial class TeamDraftPageModel(
    IGameDayClient gameDayClient,
    IGameDayNavigator navigator) : ObservableObject
{
    private Guid sessionId;
    private Guid teamId;

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _teamName = string.Empty;

    [ObservableProperty]
    private string _captainName = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public ObservableCollection<DraftPlayerItem> Players { get; } = [];

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand]
    private void TogglePick(DraftPlayerItem? item)
    {
        if (item is null || !item.CanPick)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
        UpdateSummary();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Save(CancellationToken cancellationToken)
    {
        var selected = Players.Where(item => item.IsSelected).Select(item => item.PlayerId).ToArray();
        var result = await gameDayClient.SaveTeamPicksAsync(sessionId, teamId, selected, cancellationToken);
        if (result.IsSuccess)
        {
            await LoadAsync(cancellationToken);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        foreach (var item in Players)
        {
            item.IsVisible = string.IsNullOrWhiteSpace(value)
                || item.Name.Contains(value, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var dto = await gameDayClient.GetTeamDraftAsync(sessionId == Guid.Empty ? Guid.Parse("20000000-0000-0000-0000-000000000001") : sessionId, cancellationToken);
        if (dto is null)
        {
            State = ViewState.Empty;
            return;
        }

        sessionId = dto.SessionId;
        teamId = dto.TeamId;
        TeamName = dto.TeamName;
        CaptainName = dto.CaptainName;
        var assigned = dto.Teams.SelectMany(team => team.PlayerIds.Select(playerId => (playerId, team.Name))).ToDictionary();
        var currentTeam = dto.Teams.First(team => team.TeamId == dto.TeamId);

        Players.Clear();
        foreach (var player in dto.CheckedInPlayers)
        {
            assigned.TryGetValue(player.Player.Id, out var owner);
            var isMine = currentTeam.PlayerIds.Contains(player.Player.Id);
            Players.Add(new DraftPlayerItem(
                player.Player.Id,
                player.Player.Initials,
                player.Player.DisplayName,
                isMine ? player.Detail : owner is null ? player.Detail : $"Already picked - {owner}",
                isMine,
                owner is null || isMine));
        }

        UpdateSummary();
        State = ViewState.Content;
    }

    private void UpdateSummary()
    {
        var picked = Players.Count(item => item.IsSelected);
        var unassigned = Players.Count(item => item.CanPick && !item.IsSelected);
        Summary = $"{picked} picked - {unassigned} unassigned";
    }
}

public partial class PostGameApprovalPageModel(
    IGameDayClient gameDayClient,
    IGameDayNavigator navigator) : ObservableObject
{
    private Guid sessionId;

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private int _teamCount;

    [ObservableProperty]
    private bool _needsReview;

    [ObservableProperty]
    private bool _isPublished;

    public ObservableCollection<TeamResultItem> TeamResults { get; } = [];

    public ObservableCollection<StatApprovalItem> Approvals { get; } = [];

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand]
    private void IncrementWin(TeamResultItem item) => item.TryUpdate(item.Wins + 1, item.Draws, item.Losses);

    [RelayCommand]
    private void IncrementDraw(TeamResultItem item) => item.TryUpdate(item.Wins, item.Draws + 1, item.Losses);

    [RelayCommand]
    private void IncrementLoss(TeamResultItem item) => item.TryUpdate(item.Wins, item.Draws, item.Losses + 1);

    [RelayCommand]
    private void DecrementWin(TeamResultItem item) => item.TryUpdate(Math.Max(0, item.Wins - 1), item.Draws, item.Losses);

    [RelayCommand]
    private void DecrementDraw(TeamResultItem item) => item.TryUpdate(item.Wins, Math.Max(0, item.Draws - 1), item.Losses);

    [RelayCommand]
    private void DecrementLoss(TeamResultItem item) => item.TryUpdate(item.Wins, item.Draws, Math.Max(0, item.Losses - 1));

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SaveResult(TeamResultItem item, CancellationToken cancellationToken)
    {
        await gameDayClient.SaveTeamResultAsync(
            sessionId,
            new TeamResultUpdateDto(item.TeamId, item.Wins, item.Draws, item.Losses),
            cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ApproveStat(StatApprovalItem item, CancellationToken cancellationToken)
    {
        var result = await gameDayClient.ApproveStatAsync(sessionId, item.SubmissionId, cancellationToken);
        if (result.IsSuccess)
        {
            item.Status = StatApprovalStatus.Approved;
            NeedsReview = Approvals.Any(approval => approval.Status == StatApprovalStatus.NeedsReview);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Publish(CancellationToken cancellationToken)
    {
        var result = await gameDayClient.PublishPostGameAsync(sessionId, cancellationToken);
        if (result.IsSuccess)
        {
            await LoadAsync(cancellationToken);
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var dto = await gameDayClient.GetPostGameApprovalAsync(sessionId == Guid.Empty ? Guid.Parse("20000000-0000-0000-0000-000000000001") : sessionId, cancellationToken);
        if (dto is null)
        {
            State = ViewState.Empty;
            return;
        }

        sessionId = dto.SessionId;
        TeamCount = dto.TeamCount;
        NeedsReview = dto.NeedsReview;
        IsPublished = dto.IsPublished;
        TeamResults.Clear();
        foreach (var result in dto.TeamResults)
        {
            TeamResults.Add(new TeamResultItem(result.TeamId, result.TeamName, TeamCount, result.Wins, result.Draws, result.Losses));
        }

        Approvals.Clear();
        foreach (var approval in dto.PendingApprovals)
        {
            Approvals.Add(StatApprovalItem.From(approval));
        }

        State = ViewState.Content;
    }
}

public partial class CaptainPlayerItem(Guid playerId, string initials, string name, string detail, bool isSelected) : ObservableObject
{
    public Guid PlayerId { get; } = playerId;
    public string Initials { get; } = initials;
    public string Name { get; } = name;
    public string Detail { get; } = detail;

    [ObservableProperty]
    private bool _isSelected = isSelected;

    [ObservableProperty]
    private bool _isVisible = true;
}

public partial class DraftPlayerItem(Guid playerId, string initials, string name, string detail, bool isSelected, bool canPick) : ObservableObject
{
    public Guid PlayerId { get; } = playerId;
    public string Initials { get; } = initials;
    public string Name { get; } = name;
    public string Detail { get; } = detail;
    public bool CanPick { get; } = canPick;

    [ObservableProperty]
    private bool _isSelected = isSelected;

    [ObservableProperty]
    private bool _isVisible = true;
}

public partial class TeamResultItem(Guid teamId, string teamName, int teamCount, int wins, int draws, int losses) : ObservableObject
{
    public Guid TeamId { get; } = teamId;
    public string TeamName { get; } = teamName;
    public int MaxResults { get; } = teamCount - 1;

    [ObservableProperty]
    private int _wins = wins;

    [ObservableProperty]
    private int _draws = draws;

    [ObservableProperty]
    private int _losses = losses;

    public string Detail => $"{Wins + Draws + Losses} of {MaxResults} results recorded";

    partial void OnWinsChanged(int value) => ClampTotals(nameof(Wins));

    partial void OnDrawsChanged(int value) => ClampTotals(nameof(Draws));

    partial void OnLossesChanged(int value) => ClampTotals(nameof(Losses));

    private void ClampTotals(string changedProperty)
    {
        var excess = Wins + Draws + Losses - MaxResults;
        if (excess <= 0)
        {
            OnPropertyChanged(nameof(Detail));
            return;
        }

        if (changedProperty == nameof(Wins))
        {
            Wins = Math.Max(0, Wins - excess);
        }
        else if (changedProperty == nameof(Draws))
        {
            Draws = Math.Max(0, Draws - excess);
        }
        else
        {
            Losses = Math.Max(0, Losses - excess);
        }

        OnPropertyChanged(nameof(Detail));
    }

    public bool TryUpdate(int winsValue, int drawsValue, int lossesValue)
    {
        if (winsValue + drawsValue + lossesValue > MaxResults)
        {
            return false;
        }

        Wins = winsValue;
        Draws = drawsValue;
        Losses = lossesValue;
        OnPropertyChanged(nameof(Detail));
        return true;
    }
}

public partial class StatApprovalItem(Guid submissionId, string initials, string name, string detail, StatApprovalStatus status) : ObservableObject
{
    public Guid SubmissionId { get; } = submissionId;
    public string Initials { get; } = initials;
    public string Name { get; } = name;
    public string Detail { get; } = detail;

    [ObservableProperty]
    private StatApprovalStatus _status = status;

    public bool CanApprove => Status is StatApprovalStatus.Pending or StatApprovalStatus.NeedsReview;

    public string ApprovalActionText => Status == StatApprovalStatus.NeedsReview ? "Resolve" : "Approve";

    partial void OnStatusChanged(StatApprovalStatus value)
    {
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(ApprovalActionText));
    }

    public static StatApprovalItem From(PendingStatApprovalDto dto)
    {
        var parts = new List<string>();
        if (dto.Goals > 0)
        {
            parts.Add($"{dto.Goals} {(dto.Goals == 1 ? "goal" : "goals")}");
        }

        if (dto.Assists > 0)
        {
            parts.Add($"{dto.Assists} {(dto.Assists == 1 ? "assist" : "assists")}");
        }

        return new StatApprovalItem(
            dto.SubmissionId,
            dto.Player.Initials,
            dto.Player.DisplayName,
            parts.Count == 0 ? "assist disputed" : string.Join(" - ", parts),
            dto.Status);
    }
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
public partial class CaptainAssignmentPageModel : IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query) => ApplySessionIdQuery(query);

    private void ApplySessionIdQuery(IDictionary<string, object> query)
    {
        if (query.TryGetValue("sessionId", out var value) &&
            Guid.TryParse(value?.ToString(), out var parsedSessionId))
        {
            sessionId = parsedSessionId;
        }
    }
}

public partial class TeamDraftPageModel : IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query) => ApplySessionIdQuery(query);

    private void ApplySessionIdQuery(IDictionary<string, object> query)
    {
        if (query.TryGetValue("sessionId", out var value) &&
            Guid.TryParse(value?.ToString(), out var parsedSessionId))
        {
            sessionId = parsedSessionId;
        }
    }
}

public partial class PostGameApprovalPageModel : IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query) => ApplySessionIdQuery(query);

    private void ApplySessionIdQuery(IDictionary<string, object> query)
    {
        if (query.TryGetValue("sessionId", out var value) &&
            Guid.TryParse(value?.ToString(), out var parsedSessionId))
        {
            sessionId = parsedSessionId;
        }
    }
}

public sealed class ShellGameDayNavigator : IGameDayNavigator
{
    public Task OpenCaptainAssignmentAsync(Guid sessionId) =>
        Shell.Current.GoToAsync(BuildRoute("captains", sessionId));

    public Task OpenTeamDraftAsync(Guid sessionId) =>
        Shell.Current.GoToAsync(BuildRoute("draft", sessionId));

    public Task OpenPostGameApprovalAsync(Guid sessionId) =>
        Shell.Current.GoToAsync(BuildRoute("postgame", sessionId));

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");

    private static string BuildRoute(string route, Guid sessionId) =>
        $"{route}?sessionId={Uri.EscapeDataString(sessionId.ToString())}";
}
#endif

