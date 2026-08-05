using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Clients;
using BadgeVariant = SouthBaySoccer.Controls.BadgeVariant;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

public interface IGameDayNavigator
{
    Task OpenCaptainAssignmentAsync(Guid sessionId);

    Task OpenTeamDraftAsync(Guid sessionId);

    Task OpenTeamsViewAsync(Guid sessionId);

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
    IUserDialogService? dialogService = null,
    IRosterClient? rosterClient = null,
    IProfileClient? profileClient = null,
    ILogger<GameDayPageModel>? logger = null) : ObservableObject
{
    private static readonly TimeZoneInfo VenueTimeZone = FindVenueTimeZone();
    public const string NoticeText = "RSVP is attendance intent. Game Day check-in records who is actually at the field.";
    public const string ErrorTitle = "Couldn't load Game Day";
    public const string ErrorMessage = "Something went wrong loading the active game-day flow.";
    public const string NoGameTitle = "No game today";
    public const string NoGameMessage =
        "You have no game today. Games you RSVP to — or that run in your WhatsApp group — appear here on match day.";
    public const string JoinFailedTitle = "Couldn't join";

    private Guid sessionId;
    private Guid matchId;
    // The game the player is currently viewing. Null on first load so the server auto-picks; pinned
    // after each load so reloads (and the picker) stay on the chosen game rather than jumping.
    private Guid? selectedSessionId;
    // Game-admin widening: when true the server returns every game today, not just relevant ones.
    private bool showAllGames;
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
    [NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckInCommand))]
    private bool _canCheckIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderContextLabel))]
    [NotifyPropertyChangedFor(nameof(DisplayHeaderContextLabel))]
    private string _venue = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayDateLabel))]
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
    [NotifyPropertyChangedFor(nameof(ShowMultipleGames))]
    private IReadOnlyList<GameDayGameOption> _todaysGames = [];

    public bool HasMultipleGames => TodaysGames.Count > 1;

    public bool ShowMultipleGames => HasMultipleGames && ShowTodayContent;

    /// <summary>The session's title (e.g. "Bay Area Soccer - Wednesday pickup"), for the header.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _title = "Game Day";

    /// <summary>The WhatsApp group the game runs in; null for a hand-created session.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderContextLabel))]
    [NotifyPropertyChangedFor(nameof(DisplayHeaderContextLabel))]
    [NotifyPropertyChangedFor(nameof(SpectatorBannerText))]
    private string? _groupName;

    /// <summary>"Bay Area Soccer · Marina Field" — which group and field this page is about.</summary>
    public string HeaderContextLabel => string.IsNullOrWhiteSpace(GroupName)
        ? Venue
        : $"{GroupName} · {Venue}";

    public string DisplayDateLabel => IsShowingRecentGames ? LastGame?.DateLabel ?? string.Empty : DateLabel;

    public string DisplayHeaderContextLabel => IsShowingRecentGames
        ? string.IsNullOrWhiteSpace(LastGame?.GroupName)
            ? LastGame?.Venue ?? string.Empty
            : $"{LastGame.GroupName} \u00B7 {LastGame.Venue}"
        : HeaderContextLabel;

    /// <summary>
    /// Viewing a group's game without holding a spot on it: counts and rosters are visible,
    /// every action except Join is withheld.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParticipant))]
    [NotifyPropertyChangedFor(nameof(SpectatorBannerText))]
    [NotifyPropertyChangedFor(nameof(HasJoinBlockedReason))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeVariant))]
    [NotifyPropertyChangedFor(nameof(ShowSpectatorContent))]
    private bool _isSpectator;

    public bool IsParticipant => !IsSpectator && !IsNoGame && !IsShowingRecentGames;

    public bool ShowSpectatorContent => IsSpectator && !IsShowingRecentGames;

    public bool ShowTodayContent => !IsNoGame && !IsShowingRecentGames;

    public string SpectatorBannerText => string.IsNullOrWhiteSpace(GroupName)
        ? "You're not on this game's list. You can see who's playing, or join below."
        : $"You're a member of {GroupName} — you're not on this game's list. You can see who's playing, or join below.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    private bool _canJoin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJoinBlockedReason))]
    private string? _joinBlockedReason;

    public bool HasJoinBlockedReason => IsSpectator && !string.IsNullOrWhiteSpace(JoinBlockedReason);

    /// <summary>Session capacity, for the Join card's "14 of 20 going" context.</summary>
    [ObservableProperty]
    private int _capacity;

    /// <summary>Game admins may widen the page to every game running today.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlayerViewSwitch))]
    private bool _canShowAllGames;

    [ObservableProperty]
    private bool _isShowingAllGames;

    public const string MyGamesLabel = "My games";
    public const string AllGamesLabel = "All games today";

    /// <summary>The admin scope switch's two options.</summary>
    public IReadOnlyList<string> GameScopeOptions { get; } = [MyGamesLabel, AllGamesLabel];

    public const string TodayViewLabel = "Today";
    public const string RecentGamesViewLabel = "Recent games";

    public IReadOnlyList<string> PlayerViewOptions { get; } = [TodayViewLabel, RecentGamesViewLabel];

    [ObservableProperty]
    private int _selectedPlayerViewIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParticipant))]
    [NotifyPropertyChangedFor(nameof(ShowSpectatorContent))]
    [NotifyPropertyChangedFor(nameof(ShowTodayContent))]
    [NotifyPropertyChangedFor(nameof(ShowMultipleGames))]
    [NotifyPropertyChangedFor(nameof(ShowRecentSummary))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBadge))]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(ShowPlayerViewSwitch))]
    [NotifyPropertyChangedFor(nameof(DisplayDateLabel))]
    [NotifyPropertyChangedFor(nameof(DisplayHeaderContextLabel))]
    private bool _isShowingRecentGames;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentGames))]
    [NotifyPropertyChangedFor(nameof(ShowPlayerViewSwitch))]
    private IReadOnlyList<RecentGameSummaryItem> _recentGameSummaries = [];

    public bool HasRecentGames => RecentGameSummaries.Count > 0;

    public bool ShowPlayerViewSwitch => !CanShowAllGames && !IsNoGame && HasRecentGames;

    public bool ShowRecentSummary => IsNoGame || IsShowingRecentGames;

    public bool ShowStatusBadge => !IsNoGame && !IsShowingRecentGames;

    public string DisplayTitle => IsShowingRecentGames ? RecentGamesViewLabel : Title;

    /// <summary>Which scope option is highlighted; kept in sync with <see cref="IsShowingAllGames"/>.</summary>
    [ObservableProperty]
    private int _selectedGameScopeIndex;

    /// <summary>Raw status backing the header badge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBadgeVariant))]
    private GameDayStatus _status = GameDayStatus.Closed;

    /// <summary>
    /// The header badge's variant, computed here rather than via XAML triggers: two triggers on the
    /// same property resolve by activation order, which made the spectator badge amber or neutral
    /// depending on which property happened to change last.
    /// </summary>
    public BadgeVariant StatusBadgeVariant => IsSpectator
        ? BadgeVariant.Neutral
        : Status switch
        {
            GameDayStatus.Open or GameDayStatus.CheckedIn => BadgeVariant.Success,
            GameDayStatus.Blocked => BadgeVariant.Warning,
            _ => BadgeVariant.Neutral,
        };

    /// <summary>No relevant game today; the page shows the last-game summary instead.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParticipant))]
    [NotifyPropertyChangedFor(nameof(ShowTodayContent))]
    [NotifyPropertyChangedFor(nameof(ShowMultipleGames))]
    [NotifyPropertyChangedFor(nameof(ShowRecentSummary))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBadge))]
    [NotifyPropertyChangedFor(nameof(ShowPlayerViewSwitch))]
    private bool _isNoGame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastGame))]
    [NotifyPropertyChangedFor(nameof(HasLastGameGroup))]
    [NotifyPropertyChangedFor(nameof(HasLastGameResult))]
    [NotifyPropertyChangedFor(nameof(LastGameCountsLabel))]
    [NotifyPropertyChangedFor(nameof(LastGameTeams))]
    [NotifyPropertyChangedFor(nameof(HasLastGameTeams))]
    [NotifyPropertyChangedFor(nameof(LastGameCanLockTeams))]
    [NotifyPropertyChangedFor(nameof(LastGameCanMatchPlayers))]
    [NotifyPropertyChangedFor(nameof(LastGameCanApprovePostGame))]
    [NotifyPropertyChangedFor(nameof(LastGameCanRateTeammates))]
    [NotifyPropertyChangedFor(nameof(HasLastGameActions))]
    [NotifyPropertyChangedFor(nameof(DisplayDateLabel))]
    [NotifyPropertyChangedFor(nameof(DisplayHeaderContextLabel))]
    [NotifyCanExecuteChangedFor(nameof(OpenLastGameRatingsCommand))]
    private LastGameSummaryDto? _lastGame;

    public bool HasLastGame => LastGame is not null;

    public bool HasLastGameGroup => !string.IsNullOrWhiteSpace(LastGame?.GroupName);

    public bool HasLastGameResult => !string.IsNullOrWhiteSpace(LastGame?.ResultSummary);

    /// <summary>"22 going · 26 waitlist · 11 checked in · 2 teams" (waitlist/teams only when present).</summary>
    public string LastGameCountsLabel
    {
        get
        {
            if (LastGame is not { } game)
            {
                return string.Empty;
            }

            var parts = new List<string>(4) { $"{game.GoingCount} going" };
            if (game.WaitlistCount > 0)
            {
                parts.Add($"{game.WaitlistCount} waitlist");
            }

            parts.Add($"{game.CheckedInCount} checked in");
            if (game.TeamCount > 0)
            {
                parts.Add($"{game.TeamCount} teams");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>The last game's team sheets; tapping one opens its member list with goal tallies.</summary>
    public IReadOnlyList<LastGameTeamDto> LastGameTeams => LastGame?.Teams ?? [];

    public bool HasLastGameTeams => LastGameTeams.Count > 0;

    // Follow-up actions on the last game, gated server-side by role and window: lock teams that were
    // never locked, match imported names, and confirm the result and goals.
    public bool LastGameCanLockTeams => LastGame?.CanLockTeams == true;

    public bool LastGameCanMatchPlayers => LastGame?.CanMatchPlayers == true;

    public bool LastGameCanApprovePostGame => LastGame?.CanApprovePostGame == true;

    public bool LastGameCanRateTeammates =>
        LastGame is { CanRateTeammates: true, MatchId: var candidateMatchId }
        && candidateMatchId != Guid.Empty;

    public bool HasLastGameActions =>
        LastGameCanLockTeams
        || LastGameCanMatchPlayers
        || LastGameCanApprovePostGame
        || LastGameCanRateTeammates;

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
    [NotifyPropertyChangedFor(nameof(ShowViewTeams))]
    [NotifyCanExecuteChangedFor(nameof(OpenCaptainAssignmentCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenTeamDraftCommand))]
    private bool _canAssignCaptains;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameDayActions))]
    [NotifyPropertyChangedFor(nameof(ShowPickTeam))]
    [NotifyPropertyChangedFor(nameof(ShowViewTeams))]
    [NotifyCanExecuteChangedFor(nameof(OpenTeamDraftCommand))]
    private bool _canDraftTeam;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowViewTeams))]
    [NotifyCanExecuteChangedFor(nameof(OpenTeamsViewCommand))]
    private bool _canViewTeams;

    /// <summary>
    /// Whether to surface the "Pick your team" entry. Admins see it throughout team setup (even
    /// before captains exist, so the guard can explain what's missing); captains see it once they can
    /// actually draft.
    /// </summary>
    public bool ShowPickTeam => CanDraftTeam || CanAssignCaptains;

    /// <summary>
    /// Read-only "View your team" entry for players who can't draft (regular players). Captains and
    /// admins use the draft screen instead, so this is hidden for them.
    /// </summary>
    public bool ShowViewTeams => CanViewTeams && !ShowPickTeam;

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
        // An unlinked participant has no profile to check in against; CanCheckIn is already false
        // for them, so this pattern-match is the guard that lets the rest of the method stay
        // non-nullable rather than a second condition to keep in sync.
        if (player is null || !CanAdminCheckInPlayer(player) || player.PlayerProfileId is not { } profileId)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var key = adminCheckInIdempotencyKeys.TryGetValue(profileId, out var existingKey)
                ? existingKey
                : adminCheckInIdempotencyKeys[profileId] = Guid.NewGuid();
            var result = await gameDayClient.AdminCheckInAsync(
                sessionId,
                profileId,
                key,
                cancellationToken);
            if (!result.IsSuccess)
            {
                adminCheckInIdempotencyKeys.Remove(profileId);
                ApplyNonContent(ViewState.Error, ErrorTitle, result.ErrorMessage ?? ErrorMessage);
                return;
            }

            // Update the row in place rather than reloading the whole screen: the roster list and any
            // open popup share this instance, so both reflect the check-in immediately and the admin
            // can keep checking people in from the popup. A repeat tap is a no-op (CanCheckIn is now
            // false, and the server treats a duplicate check-in as idempotent).
            adminCheckInIdempotencyKeys.Remove(profileId);
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

        return rosterListPresenter.ShowAsync(title, members, AdminCheckInCommand, LinkRosterMemberCommand);
    }

    /// <summary>
    /// Routes an unlinked roster row to whichever matching flow the caller is allowed to use: an
    /// admin or captain picks the real player from the directory, while everyone else can only
    /// identify themselves. Both destinations already exist; this is the entry point from the
    /// roster, where an unlinked name is actually noticed.
    /// </summary>
    [RelayCommand]
    private Task LinkRosterMember(GameDayRosterItem? member)
    {
        if (member is null || !member.IsUnlinked)
        {
            return Task.CompletedTask;
        }

        return member.CanMatch
            ? navigator.OpenAdminMatchAsync(sessionId)
            : navigator.OpenClaimSpotAsync();
    }

    /// <summary>
    /// The spectator's one action: RSVP to the group game being viewed. The server decides whether
    /// that lands as Going or Waitlisted, so a full reload — not an optimistic flip — shows the
    /// page in its new participant shape.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanJoinNow))]
    private async Task Join(CancellationToken cancellationToken)
    {
        if (rosterClient is null || !CanJoin || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await rosterClient.SetRsvpIntentAsync(sessionId, isGoing: true, cancellationToken);
            if (!result.IsSuccess)
            {
                // A failed CTA is a dialog, not a page-wide error state — the game is still viewable.
                if (dialogService is not null)
                {
                    await dialogService.ShowAlertAsync(
                        JoinFailedTitle,
                        result.ErrorMessage ?? "Something went wrong. Please try again.",
                        "OK");
                }

                return;
            }

            await LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            ApplyNonContent(ViewState.Offline, "You're offline", "Reconnect to join this game.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanJoinNow() => CanJoin && !IsBusy && rosterClient is not null;

    // Game-admin switch between "my games" and every game running today. Clearing the pinned
    // session lets the server re-pick inside the new pool instead of chasing an out-of-pool id.
    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task ToggleAllGames(CancellationToken cancellationToken)
    {
        if (!CanShowAllGames)
        {
            return Task.CompletedTask;
        }

        showAllGames = !showAllGames;
        selectedSessionId = null;
        return LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Tapping a last-game team (or its captain) opens the member list in the same popup the count
    /// tiles use, with each row showing captaincy and approved goal and assist tallies.
    /// </summary>
    [RelayCommand]
    private Task ShowLastGameTeam(LastGameTeamDto? team)
    {
        if (team is null || rosterListPresenter is null)
        {
            return Task.CompletedTask;
        }

        var members = team.Members
            .Select(member => new GameDayRosterItem(
                member.PlayerProfileId,
                member.DisplayName,
                isGuest: false,
                isWaitlist: false,
                isCheckedIn: false,
                canCheckIn: false,
                DescribeTeamMember(member),
                semanticStatusLabel: DescribeTeamMemberForAccessibility(member)))
            .ToArray();
        return rosterListPresenter.ShowAsync(team.Name, members, AdminCheckInCommand, LinkRosterMemberCommand);
    }

    private static string DescribeTeamMember(LastGameTeamMemberDto member)
    {
        var tallies = new List<string>(2);
        if (member.Goals > 0)
        {
            tallies.Add(string.Concat(Enumerable.Repeat("⚽", member.Goals)));
        }

        if (member.Assists > 0)
        {
            tallies.Add(string.Concat(Enumerable.Repeat("🦶", member.Assists)));
        }

        if (member.IsCaptain)
        {
            tallies.Insert(0, "Captain");
        }

        return string.Join(" · ", tallies);
    }

    private static string DescribeTeamMemberForAccessibility(LastGameTeamMemberDto member)
    {
        var tallies = new List<string>(3);
        if (member.IsCaptain)
        {
            tallies.Add("captain");
        }

        tallies.Add($"{member.Goals} {(member.Goals == 1 ? "goal" : "goals")}");
        tallies.Add($"{member.Assists} {(member.Assists == 1 ? "assist" : "assists")}");
        return string.Join(", ", tallies);
    }

    // Follow-up actions on the last game: each reuses the existing screen for that job, keyed by
    // the last game's session. Lock teams goes through captain assignment, where locking lives.
    [RelayCommand]
    private Task OpenLastGameCaptains() =>
        LastGame is { } game && LastGameCanLockTeams
            ? navigator.OpenCaptainAssignmentAsync(game.SessionId)
            : Task.CompletedTask;

    [RelayCommand]
    private Task OpenLastGameMatchPlayers() =>
        LastGame is { } game && LastGameCanMatchPlayers
            ? navigator.OpenAdminMatchAsync(game.SessionId)
            : Task.CompletedTask;

    [RelayCommand]
    private Task OpenLastGameApproval() =>
        LastGame is { } game && LastGameCanApprovePostGame
            ? navigator.OpenPostGameApprovalAsync(game.SessionId)
            : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanOpenLastGameRatings))]
    private Task OpenLastGameRatings() =>
        LastGame is { } game && CanOpenLastGameRatings()
            ? navigator.OpenRateTeammatesAsync(game.MatchId)
            : Task.CompletedTask;

    private bool CanOpenLastGameRatings() => LastGameCanRateTeammates;

    // Segmented-control entry point for the same switch. The control re-raises the selection when
    // ApplyContext syncs the index, so a pick that matches the current scope is a no-op.
    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task SelectGameScope(string? option, CancellationToken cancellationToken)
    {
        var wantsAll = string.Equals(option, AllGamesLabel, StringComparison.Ordinal);
        return wantsAll == showAllGames
            ? Task.CompletedTask
            : ToggleAllGames(cancellationToken);
    }

    [RelayCommand]
    private void SelectPlayerView(string? option)
    {
        var showRecent = string.Equals(option, RecentGamesViewLabel, StringComparison.Ordinal)
            && HasRecentGames;
        IsShowingRecentGames = showRecent;
        SelectedPlayerViewIndex = showRecent ? 1 : 0;
        LastGame = showRecent ? RecentGameSummaries[0].Summary : null;
    }

    [RelayCommand]
    private void SelectRecentGame(RecentGameSummaryItem? item)
    {
        if (IsShowingRecentGames && item is not null)
        {
            LastGame = item.Summary;
        }
    }

    private bool CanCheckInNow() => CanCheckIn && !IsBusy;

    private bool CanOpenCaptainAssignment() => CanAssignCaptains;

    // Executable whenever the entry is shown (ShowPickTeam); the handler guards the no-captains case.
    private bool CanOpenTeamDraft() => ShowPickTeam;

    [RelayCommand(CanExecute = nameof(CanOpenTeamsView))]
    private Task OpenTeamsView() =>
        ShowViewTeams ? navigator.OpenTeamsViewAsync(sessionId) : Task.CompletedTask;

    private bool CanOpenTeamsView() => ShowViewTeams;

    private bool CanOpenPostGameApproval() => CanApprovePostGame;

    // Both player-facing post-game screens are keyed off a real match id.
    private bool CanOpenOwnStats() => CanSubmitOwnStats && matchId != Guid.Empty;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        try
        {
            var context = await gameDayClient.GetTodayContextAsync(selectedSessionId, showAllGames, cancellationToken);
            if (context is null)
            {
                await ApplyNoGameAsync(cancellationToken);
                return;
            }

            ApplyContext(context);
            RecentGameSummaries = (await LoadRecentGameSummariesAsync(cancellationToken))
                .Select(summary => new RecentGameSummaryItem(summary))
                .ToArray();
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

    private async Task<IReadOnlyList<LastGameSummaryDto>> LoadRecentGameSummariesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await gameDayClient.GetRecentGameSummariesAsync(cancellationToken) ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // History is supplementary; today's Game Day remains usable if it cannot load. Record
            // only the exception type so diagnostics never capture player data or response bodies.
            logger?.LogWarning(
                "Recent Game Day history failed to load ({FailureType}); Today remains available.",
                ex.GetType().Name);
            return [];
        }
    }

    // No relevant game today. When the player has any recent game — or is a game admin who needs
    // the "All games today" switch — show it as content (a "here's where things stand" page); only
    // a non-admin with no history at all gets the bare empty state. A failed summary read degrades
    // to the bare state rather than erroring the whole page.
    private async Task ApplyNoGameAsync(CancellationToken cancellationToken)
    {
        LastGameSummaryDto? lastGame = null;
        try
        {
            lastGame = await gameDayClient.GetLastGameSummaryAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Deliberate: the summary is garnish on the empty state.
        }

        // A 204 today-context carries no flags, so admin-ness comes from the (cached) profile role;
        // without it the scope switch would be unreachable exactly when an admin needs it — a match
        // day where they RSVP'd nothing. The server still enforces the widening itself.
        var canShowAllGames = showAllGames || await ResolveIsAdminAsync(cancellationToken);

        if (lastGame is null && !canShowAllGames)
        {
            ApplyNonContent(ViewState.Empty, NoGameTitle, NoGameMessage);
            return;
        }

        IsNoGame = true;
        IsShowingRecentGames = false;
        SelectedPlayerViewIndex = 0;
        RecentGameSummaries = lastGame is null ? [] : [new RecentGameSummaryItem(lastGame)];
        IsSpectator = false;
        LastGame = lastGame;
        Title = NoGameTitle;
        GroupName = null;
        CanJoin = false;
        JoinBlockedReason = null;
        CanShowAllGames = canShowAllGames;
        IsShowingAllGames = showAllGames;
        SelectedGameScopeIndex = showAllGames ? 1 : 0;
        StateTitle = string.Empty;
        StateMessage = string.Empty;
        State = ViewState.Content;
    }

    private async Task<bool> ResolveIsAdminAsync(CancellationToken cancellationToken)
    {
        if (profileClient is null)
        {
            return false;
        }

        try
        {
            var profile = await profileClient.GetCurrentProfileAsync(cancellationToken);
            return PlayerRoles.IsAdministrative(profile?.Role);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The switch is a convenience; a failed profile read never breaks the page.
            return false;
        }
    }

    private void ApplyContext(GameDayContextDto context)
    {
        sessionId = context.SessionId;
        // Pin to the loaded game so a later reload (check-in, refresh) stays on it instead of
        // re-running the server's auto-pick and possibly jumping to a different game.
        selectedSessionId = context.SessionId;
        IsNoGame = false;
        IsShowingRecentGames = false;
        SelectedPlayerViewIndex = 0;
        LastGame = null;
        Title = string.IsNullOrWhiteSpace(context.Title) ? "Game Day" : context.Title;
        GroupName = context.GroupName;
        IsSpectator = context.IsSpectator;
        CanJoin = context.CanJoin;
        JoinBlockedReason = context.JoinBlockedReason;
        Capacity = context.Capacity;
        CanShowAllGames = context.CanShowAllGames;
        IsShowingAllGames = context.IsShowingAllGames;
        showAllGames = context.IsShowingAllGames;
        SelectedGameScopeIndex = context.IsShowingAllGames ? 1 : 0;
        Status = context.Status;
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
        LateCount = context.LateCount;
        // Defense in depth: the server already zeroes every action flag for a spectator, but the
        // client re-applies the rule so a stale or hand-crafted payload can't surface an action.
        var isParticipantContext = !context.IsSpectator;
        CanCheckIn = isParticipantContext && context.IsSelfCheckInAvailable;
        CanAssignCaptains = isParticipantContext && context.CanAssignCaptains;
        CanDraftTeam = isParticipantContext && context.CanDraftTeam;
        CanViewTeams = isParticipantContext && context.CanViewTeams;
        CanApprovePostGame = isParticipantContext && context.CanApprovePostGame;
        CanSubmitOwnStats = isParticipantContext && context.CanSubmitOwnStats;
        CanLateCheckIn = isParticipantContext && context.CanLateCheckIn;
        LateCheckInPlayers = isParticipantContext ? context.LateCheckInPlayers ?? [] : [];
        CanManageCheckIns = isParticipantContext && context.CanManageCheckIns;
        Roster = (context.Roster ?? [])
            .Select(entry => new GameDayRosterItem(
                entry.PlayerProfileId,
                entry.DisplayName,
                entry.IsGuest,
                entry.IsWaitlist,
                entry.IsCheckedIn,
                // An unlinked participant has no profile to check in against, so the check-in action
                // is withheld until they are matched. CanManageCheckIns is the spectator-hardened
                // local value, so spectator popups are always read-only rows.
                CanManageCheckIns && !entry.IsCheckedIn && !entry.IsUnlinked,
                entry.IsUnlinked
                    ? "Not linked to a profile"
                    : entry.IsCheckedIn ? "Checked in" : entry.IsWaitlist ? "Waitlist" : "Going",
                entry.PickupPalParticipantId,
                CanManageCheckIns))
            .ToArray();
        // All three tile counts are derived from the roster so each count always matches the popup
        // list shown when it is tapped. (Going includes checked-in going players; Checked in is the
        // subset that has arrived.)
        GoingCount = Roster.Count(item => !item.IsWaitlist);
        WaitlistCount = Roster.Count(item => item.IsWaitlist);
        CheckedInCount = Roster.Count(item => item.IsCheckedIn);
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

    // Non-content states carry no executable actions: everything a mode container gates is reset so
    // a stale flag can't leak an action (e.g. a still-executable Join) into an Error/Offline page.
    private void ApplyNonContent(ViewState state, string title, string message)
    {
        IsNoGame = false;
        IsSpectator = false;
        LastGame = null;
        CanJoin = false;
        JoinBlockedReason = null;
        GroupName = null;
        Title = "Game Day";
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

/// <summary>Display and accessibility projection for one player-facing recent-game choice.</summary>
public sealed record RecentGameSummaryItem(LastGameSummaryDto Summary)
{
    public string ContextLabel => string.IsNullOrWhiteSpace(Summary.GroupName)
        ? Summary.Venue
        : $"{Summary.GroupName} \u00B7 {Summary.Venue}";

    public string AccessibilityDescription =>
        $"{Summary.Title}, {Summary.DateLabel}, {ContextLabel}, tap to review summary";
}

/// <summary>
/// A Going or Waitlist member shown on the Game Day roster. <see cref="CanCheckIn"/> is precomputed
/// (admin can manage check-ins and the player is not yet checked in) so the row's admin action shows
/// only when relevant.
/// </summary>
public partial class GameDayRosterItem(
    Guid? playerProfileId,
    string displayName,
    bool isGuest,
    bool isWaitlist,
    bool isCheckedIn,
    bool canCheckIn,
    string statusLabel,
    string? pickupPalParticipantId = null,
    bool canManageRoster = false,
    string? semanticStatusLabel = null) : ObservableObject
{
    public Guid? PlayerProfileId { get; } = playerProfileId;

    /// <summary>The Pickup Pal identity of an unlinked participant; null once they have a profile.</summary>
    public string? PickupPalParticipantId { get; } = pickupPalParticipantId;

    /// <summary>
    /// An imported name with no profile behind it. They count toward the roster and appear in the
    /// list, but every profile-keyed action stays unavailable until someone links them.
    /// </summary>
    public bool IsUnlinked => PlayerProfileId is null;

    /// <summary>An admin or captain can link anyone on the roster to a real player.</summary>
    public bool CanMatch => IsUnlinked && canManageRoster;

    /// <summary>Everyone else can only claim themselves, never link somebody else.</summary>
    public bool CanClaim => IsUnlinked && !canManageRoster;

    public string LinkActionDescription => CanMatch
        ? $"Match {DisplayName} to a player profile"
        : $"Claim {DisplayName} as yourself";
    public string DisplayName { get; } = displayName;
    public string SemanticDescription =>
        string.IsNullOrWhiteSpace(semanticStatusLabel ?? StatusLabel)
            ? DisplayName
            : $"{DisplayName}, {semanticStatusLabel ?? StatusLabel}";
    public bool IsGuest { get; } = isGuest;
    public bool IsWaitlist { get; } = isWaitlist;

    // Mutable so a check-in (from the roster row or the popup, which share the same instance) flips
    // the row in place without a full reload.
    [ObservableProperty]
    private bool _isCheckedIn = isCheckedIn;

    [ObservableProperty]
    private bool _canCheckIn = canCheckIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SemanticDescription))]
    private string _statusLabel = statusLabel;
}

public partial class CaptainAssignmentPageModel(
    IGameDayClient gameDayClient,
    IGameDayNavigator navigator,
    IUserDialogService? dialogService = null) : ObservableObject
{
    public const string GrantFailedTitle = "Couldn't grant captains";
    public const string LockFailedTitle = "Couldn't lock teams";

    private Guid sessionId;
    // The captains already granted for this session (from the last load), used to disable the Grant
    // button once the current selection matches - and re-enable it the moment the admin changes the
    // captains or the count so they can re-cut the teams.
    private readonly List<Guid> grantedRankedCaptainIds = [];
    private int grantedCaptainCount;

    // Tap order IS the captain ranking: first tapped = 1st captain = team 1 = first snake pick.
    // Kept separately from Players (which stays in roster order) and re-ranked on every change.
    private readonly List<Guid> selectionOrder = [];

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
    [NotifyPropertyChangedFor(nameof(ShowLockHint))]
    private bool _canLockTeams;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockTeamsCommand))]
    private bool _canUnlockTeams;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GrantCommand))]
    [NotifyPropertyChangedFor(nameof(ShowLockTeams))]
    [NotifyPropertyChangedFor(nameof(ShowLockHint))]
    private bool _isLocked;

    /// <summary>
    /// The Lock button stays visible (disabled until lockable) instead of hiding: an admin looking
    /// for where to lock must always find it on this screen.
    /// </summary>
    public bool ShowLockTeams => !IsLocked;

    /// <summary>Explains a visible-but-disabled Lock button.</summary>
    public bool ShowLockHint => !IsLocked && !CanLockTeams;

    public ObservableCollection<CaptainPlayerItem> Players { get; } = [];

    public string SelectedCountText => $"{Players.Count(item => item.IsSelected)} selected / max {CaptainCount}";

    /// <summary>True once captains have been granted for this session.</summary>
    public bool HasGrantedCaptains => grantedCaptainCount > 0;

    /// <summary>
    /// True when the current selection exactly matches the captains already granted — including
    /// their rank order, since rank decides team number and snake pick order. Disables the Grant
    /// button; changing a captain, the order, or the count clears it.
    /// </summary>
    public bool IsCurrentSelectionGranted =>
        HasGrantedCaptains
        && CaptainCount == grantedCaptainCount
        && selectionOrder.Count == grantedCaptainCount
        && selectionOrder.SequenceEqual(grantedRankedCaptainIds);

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
        // Shrinking the count drops the lowest-ranked captains, keeping the earliest taps.
        foreach (var droppedId in selectionOrder.Skip(parsedCount).ToArray())
        {
            var item = Players.FirstOrDefault(player => player.PlayerId == droppedId);
            if (item is not null)
            {
                item.IsSelected = false;
            }

            selectionOrder.Remove(droppedId);
        }

        RefreshRankLabels();
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

        PruneSelectionOrder();
        item.IsSelected = !item.IsSelected;
        if (item.IsSelected)
        {
            selectionOrder.Remove(item.PlayerId);
            selectionOrder.Add(item.PlayerId);
        }
        else
        {
            // Deselecting re-ranks everyone below (2nd becomes 1st, etc.).
            selectionOrder.Remove(item.PlayerId);
        }

        RefreshRankLabels();
        NotifyGrantState();
    }

    // Selection state can be driven from outside the command (bindings, tests); ranks must always
    // describe what is actually selected, so drop stale entries before reasoning about order.
    private void PruneSelectionOrder() =>
        selectionOrder.RemoveAll(playerId =>
            Players.FirstOrDefault(player => player.PlayerId == playerId) is not { IsSelected: true });

    private void RefreshRankLabels()
    {
        var labelsById = selectionOrder
            .Select((playerId, index) => (playerId, label: RankLabel(index)))
            .ToDictionary(entry => entry.playerId, entry => entry.label);
        foreach (var player in Players)
        {
            player.RankLabel = labelsById.GetValueOrDefault(player.PlayerId, string.Empty);
        }
    }

    private static string RankLabel(int index) => index switch
    {
        0 => "1st captain",
        1 => "2nd captain",
        2 => "3rd captain",
        _ => $"{index + 1}th captain",
    };

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanGrant))]
    private async Task Grant(CancellationToken cancellationToken)
    {
        // Tap order carries through: index 0 becomes team 1's captain (1st snake pick), and so on.
        PruneSelectionOrder();
        var selected = selectionOrder.ToArray();
        if (selected.Length != CaptainCount)
        {
            return;
        }

        var result = await gameDayClient.AssignCaptainsAsync(sessionId, CaptainCount, selected, cancellationToken);
        if (result.IsSuccess)
        {
            await LoadAsync(cancellationToken);
            return;
        }

        // A rejected grant used to fail silently — the admin tapped, nothing changed, no reason
        // given (e.g. "Team count cannot change after results or stats have been recorded.").
        if (dialogService is not null)
        {
            await dialogService.ShowAlertAsync(
                GrantFailedTitle,
                result.ErrorMessage ?? "Something went wrong. Please try again.",
                "OK");
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
            return;
        }

        if (dialogService is not null)
        {
            await dialogService.ShowAlertAsync(
                LockFailedTitle,
                result.ErrorMessage ?? "Something went wrong. Please try again.",
                "OK");
        }
    }

    private bool CanExecuteLockTeams() => CanLockTeams && !IsLocked;

    // Admin unlock: revert a locked match to Draft so captains can pick again.
    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanExecuteUnlockTeams))]
    private async Task UnlockTeams(CancellationToken cancellationToken)
    {
        var result = await gameDayClient.UnlockTeamsAsync(sessionId, cancellationToken);
        if (result.IsSuccess)
        {
            await LoadAsync(cancellationToken);
        }
    }

    private bool CanExecuteUnlockTeams() => CanUnlockTeams;

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
        CanUnlockTeams = dto.CanUnlockTeams;
        IsLocked = dto.IsLocked;
        grantedRankedCaptainIds.Clear();
        // SelectedCaptainIds arrive in team-number order, which is the granted ranking.
        foreach (var id in dto.SelectedCaptainIds)
        {
            grantedRankedCaptainIds.Add(id);
        }

        grantedCaptainCount = dto.SelectedCaptainIds.Count;
        selectionOrder.Clear();
        selectionOrder.AddRange(dto.SelectedCaptainIds);
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

        RefreshRankLabels();
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
    IGameDayNavigator navigator,
    IUserDialogService? dialogService = null) : ObservableObject
{
    public const string AutoBalanceConfirmTitle = "Auto-balance teams?";
    public const string AutoBalanceConfirmMessage =
        "This replaces every team's current picks with a rating-balanced deal. Captains stay on their teams.";
    public const string AutoBalanceFailedTitle = "Couldn't balance teams";
    public const string PickFailedTitle = "Couldn't make that pick";
    public const string SaveFailedTitle = "Couldn't save the picks";
    public const string DiscardPicksTitle = "Discard unsaved picks?";
    public const string DiscardPicksMessage =
        "You have unsaved changes on this team. Switching teams will discard them.";

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
    [NotifyCanExecuteChangedFor(nameof(AutoBalanceCommand))]
    private bool _canManageAllTeams;

    [ObservableProperty]
    private Guid _selectedTeamId;

    /// <summary>Snake-draft turn banner: whose team is on the clock, from the server.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTurnBanner))]
    private string _onTheClockLabel = string.Empty;

    public bool HasTurnBanner => !string.IsNullOrWhiteSpace(OnTheClockLabel);

    /// <summary>True when the viewer may make the next snake pick (their turn, or an admin).</summary>
    [ObservableProperty]
    private bool _isMyTurn;

    /// <summary>Server-granted: game admin on a still-draft match.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AutoBalanceCommand))]
    private bool _canAutoBalance;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AutoBalanceCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isBusy;

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task TogglePick(DraftPlayerItem? item, CancellationToken cancellationToken)
    {
        // IsPickable already accounts for ownership, the turn, and this team's cap. Nothing may
        // mutate the selection while another draft mutation (pick, save, balance) is in flight.
        if (item is null || !item.IsPickable || IsBusy)
        {
            return;
        }

        // Captains draft one player at a time on their snake turn - the pick goes straight to the
        // server, which enforces the order. Admins keep the local multi-select + Save flow.
        if (!CanManageAllTeams)
        {
            await PickAsync(item, cancellationToken);
            return;
        }

        item.IsSelected = !item.IsSelected;
        RefreshCapAndPicks();
    }

    private async Task PickAsync(DraftPlayerItem item, CancellationToken cancellationToken)
    {
        if (IsBusy || item.IsSelected)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await gameDayClient.DraftPickAsync(sessionId, item.PlayerId, cancellationToken);
            if (result.IsSuccess)
            {
                await LoadAsync(cancellationToken);
                return;
            }

            if (dialogService is not null)
            {
                await dialogService.ShowAlertAsync(
                    PickFailedTitle,
                    result.ErrorMessage ?? "Something went wrong. Please try again.",
                    "OK");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Admin-only: replaces every team's picks with a rating-balanced deal; tapping again re-deals
    // (the server owns the deal number and bumps it each run, so every tap is the next variant).
    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanExecuteAutoBalance))]
    private async Task AutoBalance(CancellationToken cancellationToken)
    {
        if (dialogService is not null
            && !await dialogService.ShowConfirmationAsync(
                AutoBalanceConfirmTitle,
                AutoBalanceConfirmMessage,
                "Balance",
                "Cancel",
                cancellationToken))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await gameDayClient.AutoBalanceTeamsAsync(sessionId, cancellationToken);
            if (result.IsSuccess)
            {
                await LoadAsync(cancellationToken);
                return;
            }

            if (dialogService is not null)
            {
                await dialogService.ShowAlertAsync(
                    AutoBalanceFailedTitle,
                    result.ErrorMessage ?? "Something went wrong. Please try again.",
                    "OK");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExecuteAutoBalance() => CanManageAllTeams && CanAutoBalance && !IsBusy;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SelectTeam(Guid team, CancellationToken cancellationToken)
    {
        if (!CanManageAllTeams || team == Guid.Empty || team == teamId || IsBusy)
        {
            return;
        }

        // Switching re-projects from the last server dto, which would silently drop any toggles
        // the admin hasn't saved yet - ask first.
        if (HasUnsavedSelection
            && dialogService is not null
            && !await dialogService.ShowConfirmationAsync(
                DiscardPicksTitle,
                DiscardPicksMessage,
                "Switch team",
                "Stay",
                cancellationToken))
        {
            return;
        }

        ProjectTeam(team);
        MarkSelectedTeam(team);
    }

    /// <summary>Whether the current team's checkbox state differs from the last loaded server state.</summary>
    private bool HasUnsavedSelection
    {
        get
        {
            if (draft is null)
            {
                return false;
            }

            var serverTeam = draft.Teams.FirstOrDefault(candidate => candidate.TeamId == teamId);
            if (serverTeam is null)
            {
                return false;
            }

            var selectedIds = Players.Where(item => item.IsSelected).Select(item => item.PlayerId).ToHashSet();
            return !selectedIds.SetEquals(serverTeam.PlayerIds);
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

        IsBusy = true;
        try
        {
            var selected = Players.Where(item => item.IsSelected).Select(item => item.PlayerId).ToArray();
            var result = await gameDayClient.SaveTeamPicksAsync(sessionId, teamId, selected, cancellationToken);
            if (result.IsSuccess)
            {
                await LoadAsync(cancellationToken);
                return;
            }

            if (dialogService is not null)
            {
                await dialogService.ShowAlertAsync(
                    SaveFailedTitle,
                    result.ErrorMessage ?? "Something went wrong. Please try again.",
                    "OK");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSave() => CanPickPlayers && !IsLocked && !IsBusy;

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
        // Denominator for the per-team cap fallback: everyone in the Going + Waitlist roster.
        totalEligible = dto.CheckedInPlayers.Count;
        CanManageAllTeams = dto.CanManageAllTeams;
        OnTheClockLabel = dto.OnTheClockLabel;
        IsMyTurn = dto.IsMyTurn;
        CanAutoBalance = dto.CanAutoBalance;
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
            // A captain drafts in snake turns: only undrafted players, and only on their turn (the
            // server re-checks). Admins keep the toggle semantics, including releasing own players.
            var canPick = dto.CanPickPlayers
                && !dto.IsLocked
                && player.Player.Id != currentTeam.CaptainId
                && (dto.CanManageAllTeams
                    ? owner is null || isMine
                    : owner is null && dto.IsMyTurn);
            Players.Add(new DraftPlayerItem(
                player.Player.Id,
                player.Player.Initials,
                player.Player.DisplayName,
                isMine ? player.Detail : owner is null ? player.Detail : $"Already picked - {owner}",
                isMine,
                canPick));
        }

        RefreshCapAndPicks();
    }

    // Refreshes this team's cap, how many are on it, and each row's pickability, then the progress
    // caption. Caps are server-owned policy (TeamDraftDto.TeamCaps, by team rank) so the client and
    // server can never disagree; the local split survives only as a fallback for an older API.
    private void RefreshCapAndPicks()
    {
        var teamIndex = IndexOfCurrentTeam();
        if (draft?.TeamCaps is { } serverCaps && teamIndex < serverCaps.Count)
        {
            TeamCap = serverCaps[teamIndex];
        }
        else
        {
            var teamCount = draft?.Teams.Count ?? 0;
            var baseCap = teamCount > 0 ? totalEligible / teamCount : 0;
            var remainder = teamCount > 0 ? totalEligible % teamCount : 0;
            TeamCap = baseCap + (teamIndex < remainder ? 1 : 0);
        }

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

    // The game/session being reviewed, shown in the header so a captain/admin knows which one.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReviewSubtitle))]
    private string _gameTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReviewSubtitle))]
    private string _gameDateLabel = string.Empty;

    public string ReviewSubtitle => string.IsNullOrWhiteSpace(GameTitle)
        ? "Captain review"
        : string.IsNullOrWhiteSpace(GameDateLabel)
            ? GameTitle
            : $"{GameTitle} · {GameDateLabel}";

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
        GameTitle = dto.GameTitle;
        GameDateLabel = dto.DateLabel;
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

    /// <summary>
    /// "1st captain" / "2nd captain" / … in tap order; rank decides team number and snake-draft
    /// pick order. Empty while unselected.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRank))]
    private string _rankLabel = string.Empty;

    public bool HasRank => !string.IsNullOrEmpty(RankLabel);
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

    public Task OpenTeamsViewAsync(Guid sessionId) =>
        Shell.Current.GoToAsync(BuildRoute("teams-view", sessionId));

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
