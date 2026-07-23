using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

public interface IGameDayNavigator
{
    Task OpenCaptainAssignmentAsync(Guid sessionId);

    Task OpenTeamDraftAsync(Guid sessionId);

    Task OpenPostGameApprovalAsync(Guid sessionId);

    Task OpenMatchStatsAsync(Guid matchId);

    Task OpenRateTeammatesAsync(Guid matchId);

    Task OpenRecentGamesAsync();

    Task GoBackAsync();
}

public partial class GameDayPageModel(
    IGameDayClient gameDayClient,
    IGameDayNavigator navigator) : ObservableObject
{
    private static readonly TimeZoneInfo VenueTimeZone = FindVenueTimeZone();
    public const string NoticeText = "RSVP is attendance intent. Game Day check-in records who is actually at the field.";
    public const string ErrorTitle = "Couldn't load Game Day";
    public const string ErrorMessage = "Something went wrong loading the active game-day flow.";

    private Guid sessionId;
    private Guid matchId;
    private Guid? selfCheckInIdempotencyKey;
    private readonly Dictionary<Guid, Guid> lateCheckInIdempotencyKeys = [];
    private readonly Dictionary<Guid, Guid> adminCheckInIdempotencyKeys = [];

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckInCommand))]
    [NotifyCanExecuteChangedFor(nameof(LateCheckInCommand))]
    [NotifyCanExecuteChangedFor(nameof(AdminCheckInCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckInCommand))]
    private bool _canCheckIn;

    [ObservableProperty]
    private string _venue = string.Empty;

    [ObservableProperty]
    private string _dateLabel = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(HasLateCheckInPlayers))]
    private IReadOnlyList<GameDayPlayerDto> _lateCheckInPlayers = [];

    public bool HasLateCheckInPlayers => CanLateCheckIn && LateCheckInPlayers.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoster))]
    private IReadOnlyList<GameDayRosterItem> _roster = [];

    public bool HasRoster => Roster.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AdminCheckInCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenRecentGamesCommand))]
    private bool _canManageCheckIns;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLateCheckInPlayers))]
    [NotifyCanExecuteChangedFor(nameof(LateCheckInCommand))]
    private bool _canLateCheckIn;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LateCheckInCommand))]
    private string _lateCheckInReason = string.Empty;

    public bool HasGameDayActions =>
        CanAssignCaptains || CanDraftTeam || CanApprovePostGame || CanSubmitOwnStats;

    /// <summary>
    /// True once teams are locked and the post-game window is open for a player who was drafted,
    /// so they can report their own goals/assists (STAT-7) and rate the side they played with
    /// (STAT-8). Distinct from <see cref="CanApprovePostGame"/>, which is the captain/admin queue.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameDayActions))]
    [NotifyCanExecuteChangedFor(nameof(OpenMatchStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenRateTeammatesCommand))]
    private bool _canSubmitOwnStats;

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
            selfCheckInIdempotencyKey ??= Guid.NewGuid();
            var result = await gameDayClient.CheckInAsync(
                sessionId,
                selfCheckInIdempotencyKey.Value,
                cancellationToken);
            if (result.IsSuccess)
            {
                selfCheckInIdempotencyKey = null;
                await LoadAsync(cancellationToken);
                return;
            }

            selfCheckInIdempotencyKey = null;
            ApplyNonContent(ViewState.Error, ErrorTitle, result.ErrorMessage ?? ErrorMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            ApplyNonContent(ViewState.Offline, "You're offline", "Reconnect to check in at the field.");
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

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanLateCheckInPlayer))]
    private async Task LateCheckIn(GameDayPlayerDto? player, CancellationToken cancellationToken)
    {
        if (player is null || !CanLateCheckInPlayer(player))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var key = lateCheckInIdempotencyKeys.TryGetValue(player.PlayerProfileId, out var existingKey)
                ? existingKey
                : lateCheckInIdempotencyKeys[player.PlayerProfileId] = Guid.NewGuid();
            var result = await gameDayClient.LateCheckInAsync(
                sessionId,
                player.PlayerProfileId,
                LateCheckInReason.Trim(),
                key,
                cancellationToken);
            if (!result.IsSuccess)
            {
                lateCheckInIdempotencyKeys.Remove(player.PlayerProfileId);
                ApplyNonContent(ViewState.Error, ErrorTitle, result.ErrorMessage ?? ErrorMessage);
                return;
            }

            lateCheckInIdempotencyKeys.Remove(player.PlayerProfileId);
            LateCheckInReason = string.Empty;
            await LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            ApplyNonContent(ViewState.Offline, "You're offline", "Reconnect to record the late arrival.");
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

    private bool CanLateCheckInPlayer(GameDayPlayerDto? player) =>
        player is not null
        && CanLateCheckIn
        && !IsBusy
        && !string.IsNullOrWhiteSpace(LateCheckInReason);

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanAdminCheckInPlayer))]
    private async Task AdminCheckIn(GameDayRosterItem? player, CancellationToken cancellationToken)
    {
        if (player is null || !CanAdminCheckInPlayer(player))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var key = adminCheckInIdempotencyKeys.TryGetValue(player.PlayerProfileId, out var existingKey)
                ? existingKey
                : adminCheckInIdempotencyKeys[player.PlayerProfileId] = Guid.NewGuid();
            var result = await gameDayClient.AdminCheckInAsync(
                sessionId,
                player.PlayerProfileId,
                key,
                cancellationToken);
            if (!result.IsSuccess)
            {
                adminCheckInIdempotencyKeys.Remove(player.PlayerProfileId);
                ApplyNonContent(ViewState.Error, ErrorTitle, result.ErrorMessage ?? ErrorMessage);
                return;
            }

            // Reload so the freshly checked-in player drops out of the "needs check-in" state; a
            // repeat tap is harmless because the server treats the duplicate check-in as a no-op.
            adminCheckInIdempotencyKeys.Remove(player.PlayerProfileId);
            await LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            ApplyNonContent(ViewState.Offline, "You're offline", "Reconnect to check in players.");
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

    private bool CanAdminCheckInPlayer(GameDayRosterItem? player) =>
        player is not null
        && CanManageCheckIns
        && !player.IsCheckedIn
        && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanOpenCaptainAssignment))]
    private Task OpenCaptainAssignment() =>
        CanAssignCaptains ? navigator.OpenCaptainAssignmentAsync(sessionId) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanOpenTeamDraft))]
    private Task OpenTeamDraft() =>
        CanDraftTeam ? navigator.OpenTeamDraftAsync(sessionId) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanOpenPostGameApproval))]
    private Task OpenPostGameApproval() =>
        CanApprovePostGame ? navigator.OpenPostGameApprovalAsync(sessionId) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanOpenOwnStats))]
    private Task OpenMatchStats() =>
        CanOpenOwnStats() ? navigator.OpenMatchStatsAsync(matchId) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanOpenOwnStats))]
    private Task OpenRateTeammates() =>
        CanOpenOwnStats() ? navigator.OpenRateTeammatesAsync(matchId) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanManageCheckIns))]
    private Task OpenRecentGames() =>
        CanManageCheckIns ? navigator.OpenRecentGamesAsync() : Task.CompletedTask;

    private bool CanCheckInNow() => CanCheckIn && !IsBusy;

    private bool CanOpenCaptainAssignment() => CanAssignCaptains;

    private bool CanOpenTeamDraft() => CanDraftTeam;

    private bool CanOpenPostGameApproval() => CanApprovePostGame;

    // Both player-facing post-game screens are keyed off a real match id.
    private bool CanOpenOwnStats() => CanSubmitOwnStats && matchId != Guid.Empty;

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

            ApplyContext(context);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            ApplyNonContent(ViewState.Offline, "You're offline", "Reconnect to check in at the field.");
        }
        catch (Exception)
        {
            ApplyNonContent(ViewState.Error, ErrorTitle, ErrorMessage);
        }
    }

    private void ApplyContext(GameDayContextDto context)
    {
        sessionId = context.SessionId;
        matchId = context.MatchId;
        Venue = context.Venue;
        ApplyTimeLabels(context);
        StatusLabel = context.StatusLabel;
        PrimaryActionText = context.PrimaryActionText;
        BlockReason = context.BlockReason;
        GoingCount = context.GoingCount;
        CheckedInCount = context.CheckedInCount;
        LateCount = context.LateCount;
        CanCheckIn = context.IsSelfCheckInAvailable;
        CanAssignCaptains = context.CanAssignCaptains;
        CanDraftTeam = context.CanDraftTeam;
        CanApprovePostGame = context.CanApprovePostGame;
        CanSubmitOwnStats = context.CanSubmitOwnStats;
        CanLateCheckIn = context.CanLateCheckIn;
        LateCheckInPlayers = context.LateCheckInPlayers ?? [];
        CanManageCheckIns = context.CanManageCheckIns;
        Roster = (context.Roster ?? [])
            .Select(entry => new GameDayRosterItem(
                entry.PlayerProfileId,
                entry.DisplayName,
                entry.IsGuest,
                entry.IsWaitlist,
                entry.IsCheckedIn,
                context.CanManageCheckIns && !entry.IsCheckedIn,
                entry.IsCheckedIn ? "Checked in" : entry.IsWaitlist ? "Waitlist" : "Going"))
            .ToArray();
        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

    private void ApplyTimeLabels(GameDayContextDto context)
    {
        if (context.StartsAtUtc is not { } startsAtUtc
            || context.CheckInOpensAtUtc is not { } opensAtUtc
            || context.CheckInClosesAtUtc is not { } closesAtUtc)
        {
            DateLabel = context.DateLabel;
            GameStartLabel = context.GameStartLabel;
            CheckInWindowLabel = context.CheckInWindowLabel;
            CheckInCloseLabel = context.CheckInCloseLabel;
            return;
        }

        var localStart = ToVenueLocal(startsAtUtc);
        var localOpen = ToVenueLocal(opensAtUtc);
        var localClose = ToVenueLocal(closesAtUtc);
        DateLabel = localStart.ToString("ddd MMM d", CultureInfo.InvariantCulture);
        GameStartLabel = localStart.ToString("h:mm tt", CultureInfo.InvariantCulture);
        CheckInWindowLabel = $"{localOpen:h:mm tt} - {localClose:h:mm tt}";
        CheckInCloseLabel = $"closes {localClose:h:mm tt}";
    }

    private static DateTime ToVenueLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            VenueTimeZone);

    private static TimeZoneInfo FindVenueTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        }
    }

    private void ApplyNonContent(ViewState state, string title, string message)
    {
        State = state;
        StateTitle = title;
        StateMessage = message;
    }
}

/// <summary>
/// A Going or Waitlist member shown on the Game Day roster. <see cref="CanCheckIn"/> is precomputed
/// (admin can manage check-ins and the player is not yet checked in) so the row's admin action shows
/// only when relevant.
/// </summary>
public sealed record GameDayRosterItem(
    Guid PlayerProfileId,
    string DisplayName,
    bool IsGuest,
    bool IsWaitlist,
    bool IsCheckedIn,
    bool CanCheckIn,
    string StatusLabel);

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LockTeamsCommand))]
    private bool _canLockTeams;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GrantCommand))]
    private bool _isLocked;

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
        GrantCommand.NotifyCanExecuteChanged();
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
        GrantCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanGrant))]
    private async Task Grant(CancellationToken cancellationToken)
    {
        var selected = Players.Where(item => item.IsSelected).Select(item => item.PlayerId).ToArray();
        if (selected.Length != CaptainCount)
        {
            return;
        }

        var result = await gameDayClient.AssignCaptainsAsync(sessionId, CaptainCount, selected, cancellationToken);
        if (result.IsSuccess)
        {
            await LoadAsync(cancellationToken);
        }
    }

    private bool CanGrant() =>
        !IsLocked && Players.Count(item => item.IsSelected) == CaptainCount;

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanExecuteLockTeams))]
    private async Task LockTeams(CancellationToken cancellationToken)
    {
        var result = await gameDayClient.LockTeamsAsync(sessionId, cancellationToken);
        if (result.IsSuccess)
        {
            await LoadAsync(cancellationToken);
        }
    }

    private bool CanExecuteLockTeams() => CanLockTeams && !IsLocked;

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
        CanLockTeams = dto.CanLockTeams;
        IsLocked = dto.IsLocked;
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
        GrantCommand.NotifyCanExecuteChanged();
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
    private TeamDraftDto? draft;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _canPickPlayers;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLocked;

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

    /// <summary>
    /// Selectable teams for the admin/coordinator team switcher. Empty for a captain, who is
    /// locked to their own team.
    /// </summary>
    public ObservableCollection<DraftTeamOption> Teams { get; } = [];

    [ObservableProperty]
    private bool _canManageAllTeams;

    [ObservableProperty]
    private Guid _selectedTeamId;

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

    [RelayCommand]
    private void SelectTeam(Guid team)
    {
        if (CanManageAllTeams && team != Guid.Empty && team != teamId)
        {
            ProjectTeam(team);
            MarkSelectedTeam(team);
        }
    }

    private void MarkSelectedTeam(Guid selectedTeamId)
    {
        foreach (var option in Teams)
        {
            option.IsSelected = option.TeamId == selectedTeamId;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanSave))]
    private async Task Save(CancellationToken cancellationToken)
    {
        if (!CanSave())
        {
            return;
        }

        var selected = Players.Where(item => item.IsSelected).Select(item => item.PlayerId).ToArray();
        var result = await gameDayClient.SaveTeamPicksAsync(sessionId, teamId, selected, cancellationToken);
        if (result.IsSuccess)
        {
            await LoadAsync(cancellationToken);
        }
    }

    private bool CanSave() => CanPickPlayers && !IsLocked;

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

        draft = dto;
        sessionId = dto.SessionId;
        CanManageAllTeams = dto.CanManageAllTeams;
        Teams.Clear();
        foreach (var team in dto.Teams)
        {
            Teams.Add(new DraftTeamOption(team.TeamId, team.Name, team.CaptainName));
        }

        ProjectTeam(dto.TeamId);
        MarkSelectedTeam(dto.TeamId);
        State = ViewState.Content;
    }

    // Re-projects the roster for the given team without a network round-trip. A captain always sees
    // their own team; an admin/coordinator can switch between every team returned by the draft query.
    private void ProjectTeam(Guid targetTeamId)
    {
        if (draft is not { } dto)
        {
            return;
        }

        var currentTeam = dto.Teams.FirstOrDefault(team => team.TeamId == targetTeamId) ?? dto.Teams.First();
        teamId = currentTeam.TeamId;
        SelectedTeamId = currentTeam.TeamId;
        TeamName = currentTeam.Name;
        CaptainName = currentTeam.CaptainName;
        CanPickPlayers = dto.CanPickPlayers;
        IsLocked = dto.IsLocked;
        var assigned = dto.Teams.SelectMany(team => team.PlayerIds.Select(playerId => (playerId, team.Name))).ToDictionary();

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
                dto.CanPickPlayers
                    && !dto.IsLocked
                    && player.Player.Id != currentTeam.CaptainId
                    && (owner is null || isMine)));
        }

        UpdateSummary();
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
    [NotifyPropertyChangedFor(nameof(CanEditPostGame))]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(IsReadOnlyPostGame))]
    [NotifyCanExecuteChangedFor(nameof(SaveResultCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApproveStatCommand))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _canApprove;

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private int _teamCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _needsReview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditPostGame))]
    [NotifyPropertyChangedFor(nameof(CanPublish))]
    [NotifyPropertyChangedFor(nameof(IsReadOnlyPostGame))]
    [NotifyCanExecuteChangedFor(nameof(SaveResultCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApproveStatCommand))]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _isPublished;

    public bool CanEditPostGame => CanApprove && !IsPublished;

    public bool CanPublish => CanEditPostGame && !NeedsReview;

    public bool IsReadOnlyPostGame => !CanEditPostGame;

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

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanEditPostGame))]
    private async Task SaveResult(TeamResultItem item, CancellationToken cancellationToken)
    {
        if (!CanEditPostGame)
        {
            return;
        }

        await gameDayClient.SaveTeamResultAsync(
            sessionId,
            new TeamResultUpdateDto(item.TeamId, item.Wins, item.Draws, item.Losses),
            cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanEditPostGame))]
    private async Task ApproveStat(StatApprovalItem item, CancellationToken cancellationToken)
    {
        if (!CanEditPostGame)
        {
            return;
        }

        var result = await gameDayClient.ApproveStatAsync(sessionId, item.SubmissionId, cancellationToken);
        if (result.IsSuccess)
        {
            item.Status = StatApprovalStatus.Approved;
            NeedsReview = Approvals.Any(approval => approval.Status == StatApprovalStatus.NeedsReview);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanPublish))]
    private async Task Publish(CancellationToken cancellationToken)
    {
        if (!CanPublish)
        {
            return;
        }

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
        CanApprove = dto.CanApprove;
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

public partial class DraftTeamOption(Guid teamId, string name, string captainName) : ObservableObject
{
    public Guid TeamId { get; } = teamId;
    public string Name { get; } = name;
    public string CaptainName { get; } = captainName;

    /// <summary>Drives the solid/outline swap so the team being drafted for is obvious.</summary>
    [ObservableProperty]
    private bool _isSelected;
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
    /// <summary>
    /// Two teams play each other, so their records mirror and one game each is the norm; with three
    /// or four teams the night is a rotation and a side can play far more games than it has
    /// opponents, so the counters are only floored at zero.
    /// </summary>
    private const int RotationCeiling = 30;

    public Guid TeamId { get; } = teamId;
    public string TeamName { get; } = teamName;
    public int TeamCount { get; } = teamCount;

    [ObservableProperty]
    private int _wins = wins;

    [ObservableProperty]
    private int _draws = draws;

    [ObservableProperty]
    private int _losses = losses;

    public int GamesRecorded => Wins + Draws + Losses;

    public string Detail => GamesRecorded == 1
        ? "1 game recorded"
        : $"{GamesRecorded} games recorded";

    partial void OnWinsChanged(int value) => NotifyTotals();

    partial void OnDrawsChanged(int value) => NotifyTotals();

    partial void OnLossesChanged(int value) => NotifyTotals();

    private void NotifyTotals()
    {
        OnPropertyChanged(nameof(GamesRecorded));
        OnPropertyChanged(nameof(Detail));
    }

    public bool TryUpdate(int winsValue, int drawsValue, int lossesValue)
    {
        if (winsValue < 0 || drawsValue < 0 || lossesValue < 0)
        {
            return false;
        }

        // A sanity ceiling only, to stop a stuck stepper running away - not a fixture-count rule.
        if (winsValue + drawsValue + lossesValue > RotationCeiling)
        {
            return false;
        }

        Wins = winsValue;
        Draws = drawsValue;
        Losses = lossesValue;
        NotifyTotals();
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

        if (dto.AssistPlayer is not null)
        {
            parts.Add($"assist: {dto.AssistPlayer.DisplayName}");
        }

        return new StatApprovalItem(
            dto.SubmissionId,
            dto.Player.Initials,
            dto.Player.DisplayName,
            parts.Count == 0
                ? string.IsNullOrWhiteSpace(dto.Detail) ? "Stat submission" : dto.Detail
                : string.Join(" - ", parts),
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

    public Task OpenMatchStatsAsync(Guid matchId) =>
        Shell.Current.GoToAsync($"matchstats?matchId={Uri.EscapeDataString(matchId.ToString())}");

    // The rater is resolved server-side from the bearer token (INV-8), so only the match travels.
    public Task OpenRateTeammatesAsync(Guid matchId) =>
        Shell.Current.GoToAsync($"rate-teammates?matchId={Uri.EscapeDataString(matchId.ToString())}");

    public Task OpenRecentGamesAsync() => Shell.Current.GoToAsync("recent-games");

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");

    private static string BuildRoute(string route, Guid sessionId) =>
        $"{route}?sessionId={Uri.EscapeDataString(sessionId.ToString())}";
}
#endif
