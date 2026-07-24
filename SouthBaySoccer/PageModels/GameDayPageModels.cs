using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Services;
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

    Task OpenClaimSpotAsync();

    Task OpenAdminMatchAsync(Guid sessionId);

    Task GoBackAsync();
}

public partial class GameDayPageModel(
    IGameDayClient gameDayClient,
    IGameDayNavigator navigator,
    IRosterListPresenter? rosterListPresenter = null,
    IUserDialogService? dialogService = null) : ObservableObject
{
    private static readonly TimeZoneInfo VenueTimeZone = FindVenueTimeZone();
    public const string NoticeText = "RSVP is attendance intent. Game Day check-in records who is actually at the field.";
    public const string ErrorTitle = "Couldn't load Game Day";
    public const string ErrorMessage = "Something went wrong loading the active game-day flow.";

    private Guid sessionId;
    private Guid matchId;
    // The game the player is currently viewing. Null on first load so the server auto-picks; pinned
    // after each load so reloads (and the picker) stay on the chosen game rather than jumping.
    private Guid? selectedSessionId;
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
    private int _waitlistCount;

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

    // Category slices of the roster, read live when a count is tapped so they reflect check-ins.
    public IReadOnlyList<GameDayRosterItem> GoingRoster => [.. Roster.Where(item => !item.IsWaitlist)];

    public IReadOnlyList<GameDayRosterItem> WaitlistRoster => [.. Roster.Where(item => item.IsWaitlist)];

    public IReadOnlyList<GameDayRosterItem> CheckedInRoster => [.. Roster.Where(item => item.IsCheckedIn)];

    /// <summary>Today's games the player can act on; the picker shows only when more than one runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleGames))]
    private IReadOnlyList<GameDayGameOption> _todaysGames = [];

    public bool HasMultipleGames => TodaysGames.Count > 1;

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
    [NotifyPropertyChangedFor(nameof(ShowPickTeam))]
    [NotifyCanExecuteChangedFor(nameof(OpenCaptainAssignmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenTeamDraftCommand))]
    private bool _canAssignCaptains;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameDayActions))]
    [NotifyPropertyChangedFor(nameof(ShowPickTeam))]
    [NotifyCanExecuteChangedFor(nameof(OpenTeamDraftCommand))]
    private bool _canDraftTeam;

    /// <summary>
    /// Whether to surface the "Pick your team" entry. Admins see it throughout team setup (even
    /// before captains exist, so the guard can explain what's missing); captains see it once they can
    /// actually draft.
    /// </summary>
    public bool ShowPickTeam => CanDraftTeam || CanAssignCaptains;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameDayActions))]
    [NotifyCanExecuteChangedFor(nameof(OpenPostGameApprovalCommand))]
    private bool _canApprovePostGame;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Retry(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    // Picker tap: load a different one of today's games. No-op if it is already showing.
    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task SelectGame(Guid gameSessionId, CancellationToken cancellationToken)
    {
        if (gameSessionId == Guid.Empty || gameSessionId == sessionId)
        {
            return Task.CompletedTask;
        }

        selectedSessionId = gameSessionId;
        return LoadAsync(cancellationToken);
    }

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

            // Update the row in place rather than reloading the whole screen: the roster list and any
            // open popup share this instance, so both reflect the check-in immediately and the admin
            // can keep checking people in from the popup. A repeat tap is a no-op (CanCheckIn is now
            // false, and the server treats a duplicate check-in as idempotent).
            adminCheckInIdempotencyKeys.Remove(player.PlayerProfileId);
            player.IsCheckedIn = true;
            player.CanCheckIn = false;
            player.StatusLabel = "Checked in";
            CheckedInCount++;
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

    public const string NoCaptainsTitle = "Assign captains first";
    public const string NoCaptainsMessage =
        "Teams can't be picked until captains are assigned. Tap \"Assign captains\" to set them up first.";

    [RelayCommand(CanExecute = nameof(CanOpenTeamDraft))]
    private async Task OpenTeamDraft()
    {
        // Only navigate once captains exist; otherwise the draft has no teams to fill, so warn instead.
        if (!CanDraftTeam)
        {
            if (dialogService is not null)
            {
                await dialogService.ShowAlertAsync(NoCaptainsTitle, NoCaptainsMessage, "OK");
            }

            return;
        }

        await navigator.OpenTeamDraftAsync(sessionId);
    }

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

    [RelayCommand]
    private Task OpenClaimSpot() => navigator.OpenClaimSpotAsync();

    // Tapping a Going / Waitlist / Checked-in count opens a popup listing those people. The lists are
    // read live so they reflect any check-ins made this session.
    [RelayCommand]
    private Task ShowRoster(string? category)
    {
        if (rosterListPresenter is null)
        {
            return Task.CompletedTask;
        }

        var (title, members) = category switch
        {
            "Waitlist" => ("Waitlist", WaitlistRoster),
            "CheckedIn" => ("Checked in", CheckedInRoster),
            _ => ("Going", GoingRoster),
        };

        return rosterListPresenter.ShowAsync(title, members, AdminCheckInCommand);
    }

    private bool CanCheckInNow() => CanCheckIn && !IsBusy;

    private bool CanOpenCaptainAssignment() => CanAssignCaptains;

    // Executable whenever the entry is shown (ShowPickTeam); the handler guards the no-captains case.
    private bool CanOpenTeamDraft() => ShowPickTeam;

    private bool CanOpenPostGameApproval() => CanApprovePostGame;

    // Both player-facing post-game screens are keyed off a real match id.
    private bool CanOpenOwnStats() => CanSubmitOwnStats && matchId != Guid.Empty;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        try
        {
            var context = await gameDayClient.GetTodayContextAsync(selectedSessionId, cancellationToken);
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
        // Pin to the loaded game so a later reload (check-in, refresh) stays on it instead of
        // re-running the server's auto-pick and possibly jumping to a different game.
        selectedSessionId = context.SessionId;
        TodaysGames = (context.TodaysGames ?? [])
            .Select(game => new GameDayGameOption(
                game.SessionId,
                game.Title,
                game.Venue,
                FormatGameTime(game.StartsAtUtc),
                game.StatusLabel,
                game.IsSelected))
            .ToArray();
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
        // Derived from the roster so the count always matches the popup list shown on tap.
        WaitlistCount = Roster.Count(item => item.IsWaitlist);
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

    // Short local kick-off label for a picker chip, e.g. "Thu 7:30 PM".
    private static string FormatGameTime(DateTime startsAtUtc) =>
        ToVenueLocal(startsAtUtc).ToString("ddd h:mm tt", CultureInfo.InvariantCulture);

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

/// <summary>One of today's games shown in the Game Day picker (rendered only when more than one).</summary>
public sealed record GameDayGameOption(
    Guid SessionId,
    string Title,
    string Venue,
    string TimeLabel,
    string StatusLabel,
    bool IsSelected);

/// <summary>
/// A Going or Waitlist member shown on the Game Day roster. <see cref="CanCheckIn"/> is precomputed
/// (admin can manage check-ins and the player is not yet checked in) so the row's admin action shows
/// only when relevant.
/// </summary>
public partial class GameDayRosterItem(
    Guid playerProfileId,
    string displayName,
    bool isGuest,
    bool isWaitlist,
    bool isCheckedIn,
    bool canCheckIn,
    string statusLabel) : ObservableObject
{
    public Guid PlayerProfileId { get; } = playerProfileId;
    public string DisplayName { get; } = displayName;
    public bool IsGuest { get; } = isGuest;
    public bool IsWaitlist { get; } = isWaitlist;

    // Mutable so a check-in (from the roster row or the popup, which share the same instance) flips
    // the row in place without a full reload.
    [ObservableProperty]
    private bool _isCheckedIn = isCheckedIn;

    [ObservableProperty]
    private bool _canCheckIn = canCheckIn;

    [ObservableProperty]
    private string _statusLabel = statusLabel;
}

public partial class CaptainAssignmentPageModel(
    IGameDayClient gameDayClient,
    IGameDayNavigator navigator) : ObservableObject
{
    private Guid sessionId;
    // The captains already granted for this session (from the last load), used to disable the Grant
    // button once the current selection matches - and re-enable it the moment the admin changes the
    // captains or the count so they can re-cut the teams.
    private readonly HashSet<Guid> grantedCaptainIds = [];
    private int grantedCaptainCount;

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCountText))]
    [NotifyPropertyChangedFor(nameof(IsTwoCaptains))]
    [NotifyPropertyChangedFor(nameof(IsThreeCaptains))]
    [NotifyPropertyChangedFor(nameof(IsFourCaptains))]
    private int _captainCount = 2;

    /// <summary>Drives the active-state styling on the captain-count selector buttons.</summary>
    public bool IsTwoCaptains => CaptainCount == 2;

    public bool IsThreeCaptains => CaptainCount == 3;

    public bool IsFourCaptains => CaptainCount == 4;

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

    /// <summary>True once captains have been granted for this session.</summary>
    public bool HasGrantedCaptains => grantedCaptainCount > 0;

    /// <summary>
    /// True when the current selection exactly matches the captains already granted, so re-granting
    /// would be a no-op. Disables the Grant button; changing a captain or the count clears it.
    /// </summary>
    public bool IsCurrentSelectionGranted =>
        HasGrantedCaptains
        && CaptainCount == grantedCaptainCount
        && Players.Count(item => item.IsSelected) == grantedCaptainCount
        && Players.Where(item => item.IsSelected).Select(item => item.PlayerId).ToHashSet().SetEquals(grantedCaptainIds);

    public bool HasGrantStatus => HasGrantedCaptains;

    public string GrantStatusText =>
        !HasGrantedCaptains
            ? string.Empty
            : IsCurrentSelectionGranted
                ? $"Captain permissions granted to {grantedCaptainCount} captains."
                : "Captains changed - grant again to re-cut the teams (this releases current picks).";

    private void NotifyGrantState()
    {
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(HasGrantedCaptains));
        OnPropertyChanged(nameof(IsCurrentSelectionGranted));
        OnPropertyChanged(nameof(HasGrantStatus));
        OnPropertyChanged(nameof(GrantStatusText));
        GrantCommand.NotifyCanExecuteChanged();
    }

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

        NotifyGrantState();
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
        NotifyGrantState();
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

    // Grant is available when the right number of captains is picked and locking hasn't happened -
    // and the selection differs from what's already granted, so the button greys out to show the
    // current captains are granted, then re-enables once the admin changes the captains or the count.
    private bool CanGrant() =>
        !IsLocked
        && Players.Count(item => item.IsSelected) == CaptainCount
        && !IsCurrentSelectionGranted;

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
        grantedCaptainIds.Clear();
        foreach (var id in dto.SelectedCaptainIds)
        {
            grantedCaptainIds.Add(id);
        }

        grantedCaptainCount = dto.SelectedCaptainIds.Count;
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
        NotifyGrantState();
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
    private int totalEligible;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _canPickPlayers;

    /// <summary>This team's share of the roster (total eligible divided across the teams, extras first).</summary>
    [ObservableProperty]
    private int _teamCap;

    /// <summary>Players currently on this team, including its captain.</summary>
    [ObservableProperty]
    private int _selectedCount;

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
        // IsPickable already accounts for ownership and this team's cap, so this both blocks picking
        // past the cap and leaves already-picked players releasable.
        if (item is null || !item.IsPickable)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
        RefreshCapAndPicks();
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
        // Denominator for the per-team cap: everyone in the Going + Waitlist eligible roster.
        totalEligible = dto.CheckedInPlayers.Count;
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

        RefreshCapAndPicks();
    }

    // Recomputes this team's cap (its share of the roster), how many are on it, and each row's
    // pickability, then refreshes the progress caption. Runs after every projection and toggle so
    // the count tracks the roster - a fresh load picks up players who RSVP'd since it last opened.
    private void RefreshCapAndPicks()
    {
        var teamCount = draft?.Teams.Count ?? 0;
        var baseCap = teamCount > 0 ? totalEligible / teamCount : 0;
        var remainder = teamCount > 0 ? totalEligible % teamCount : 0;
        // Distribute the remainder one extra at a time to the first teams, so with 16 players and 3
        // teams the caps are 6, 5, 5 rather than everyone capped at 5 and one player left over.
        TeamCap = baseCap + (IndexOfCurrentTeam() < remainder ? 1 : 0);

        SelectedCount = Players.Count(item => item.IsSelected);
        var isFull = SelectedCount >= TeamCap;
        foreach (var item in Players)
        {
            item.IsPickable = item.CanPick && (item.IsSelected || !isFull);
        }

        Summary = isFull
            ? $"{SelectedCount} of {TeamCap} selected (full)"
            : $"{SelectedCount} of {TeamCap} selected ({TeamCap - SelectedCount} left)";
    }

    private int IndexOfCurrentTeam()
    {
        if (draft is null)
        {
            return 0;
        }

        for (var index = 0; index < draft.Teams.Count; index++)
        {
            if (draft.Teams[index].TeamId == teamId)
            {
                return index;
            }
        }

        return 0;
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

    /// <summary>
    /// Explains why a scoreline will not publish. Every game produces exactly one win and one loss,
    /// and a draw is recorded by both sides, so the totals have to satisfy both identities before
    /// the match can complete - without this the screen just silently refused to finish.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultsHint))]
    private string _resultsHint = string.Empty;

    public bool HasResultsHint => ResultsHint.Length > 0;

    private void RefreshResultsHint()
    {
        var wins = TeamResults.Sum(x => x.Wins);
        var losses = TeamResults.Sum(x => x.Losses);
        var draws = TeamResults.Sum(x => x.Draws);

        if (wins + losses + draws == 0)
        {
            ResultsHint = "No results recorded yet. Add each team's wins, draws, and losses.";
            return;
        }

        if (wins != losses)
        {
            ResultsHint = $"{wins} wins vs {losses} losses - every game has one winner and one loser, so these must match.";
            return;
        }

        if (draws % 2 != 0)
        {
            ResultsHint = $"{draws} draws - a draw is recorded by both teams, so the total must be even.";
            return;
        }

        ResultsHint = string.Empty;
    }

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
            var item = new TeamResultItem(result.TeamId, result.TeamName, TeamCount, result.Wins, result.Draws, result.Losses);
            item.PropertyChanged += (_, _) => RefreshResultsHint();
            TeamResults.Add(item);
        }

        Approvals.Clear();
        foreach (var approval in dto.PendingApprovals)
        {
            Approvals.Add(StatApprovalItem.From(approval));
        }

        RefreshResultsHint();
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

    /// <summary>Base eligibility fixed at projection: unowned (or on this team), not the captain, not locked.</summary>
    public bool CanPick { get; } = canPick;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmed))]
    private bool _isSelected = isSelected;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>
    /// Whether tapping the row does anything right now - narrower than <see cref="CanPick"/>: also
    /// false once this team has filled its share of the roster. An already-picked player stays
    /// pickable so the captain (or admin) can release them.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmed))]
    private bool _isPickable = canPick;

    /// <summary>Greys the row: not on this team and not pickable (taken by another team, or team full).</summary>
    public bool IsDimmed => !IsSelected && !IsPickable;
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

    public Task OpenClaimSpotAsync() => Shell.Current.GoToAsync("claim-spot");

    public Task OpenAdminMatchAsync(Guid sessionId) =>
        Shell.Current.GoToAsync($"admin-match?sessionId={Uri.EscapeDataString(sessionId.ToString())}");

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");

    private static string BuildRoute(string route, Guid sessionId) =>
        $"{route}?sessionId={Uri.EscapeDataString(sessionId.ToString())}";
}
#endif
