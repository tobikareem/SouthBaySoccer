using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Controls;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Loads and presents the signed-in player's profile.
/// </summary>
public partial class ProfilePageModel(
    IProfileClient profileClient,
    IGroupsClient groupsClient,
    IProfileExternalLauncher externalLauncher,
    IProfileNavigator navigator,
    IAuthenticationCoordinator authenticationCoordinator,
    IUserDialogService dialogService,
    IClientResponseCache responseCache) : ObservableObject
{
    public const string EmptyTitle = "Profile not found";
    public const string EmptyMessage = "Your profile data is not available.";
    public const string ErrorTitle = "Couldn't load your profile";
    public const string ErrorMessage = "Something went wrong loading your profile. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load your profile.";
    public const string ExternalLaunchError = "Pickup Pal could not be opened. Please try again.";
    public const string PlayerIdQueryKey = "playerId";
    public const string SignOutConfirmTitle = "Sign out?";
    public const string SignOutConfirmMessage =
        "You'll be signed out on this device. Sign in again with any phone number connected to a Pickup Pal account.";

    private Guid? requestedPlayerId;

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private PlayerProfileDto? _profile;

    [ObservableProperty]
    private IReadOnlyList<ProfileFormBadge> _recentForm = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingNote))]
    private string _pendingNote = string.Empty;

    [ObservableProperty]
    private string _actionMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The signed-in player's memberships, shown as status pills in the "My groups" card.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MyGroupsSummary))]
    private IReadOnlyList<MembershipRowItem> _myGroups = [];

    /// <summary>Groups the player administers — one "Manage members" row each.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdminGroups))]
    private IReadOnlyList<AdminGroupItem> _adminGroups = [];

    /// <summary>Set by the server for the two owners; unlocks the "Groups &amp; admins" card.</summary>
    [ObservableProperty]
    private bool _isSuperAdmin;

    public bool HasAdminGroups => AdminGroups.Count > 0;

    /// <summary>"Bay Area Soccer, Morning Pick Up Soccer" or an invitation to join when there are none.</summary>
    public string MyGroupsSummary =>
        MyGroups.Count == 0 ? "You're not in a group yet." : string.Join(", ", MyGroups.Select(group => group.Name));

    /// <summary>"1 approved · 1 pending" style counts under the summary.</summary>
    public string MyGroupsDetail
    {
        get
        {
            if (MyGroups.Count == 0)
            {
                return "Join the groups you play with.";
            }

            var parts = new List<string>(3);
            var approved = MyGroups.Count(group => group.IsApproved);
            var pending = MyGroups.Count(group => group.IsPending);
            var declined = MyGroups.Count(group => group.IsDeclined);
            if (approved > 0) parts.Add($"{approved} approved");
            if (pending > 0) parts.Add($"{pending} pending");
            if (declined > 0) parts.Add($"{declined} declined");
            return string.Join(" · ", parts);
        }
    }

    public bool HasPendingNote => !string.IsNullOrWhiteSpace(PendingNote);

    public bool HasActionMessage => !string.IsNullOrWhiteSpace(ActionMessage);

    public bool CanEditProfile => requestedPlayerId is null;

    /// <summary>
    /// True when this is another player's profile, opened as a pushed detail page. Drives the back
    /// affordance, since the profile pages run with the Shell nav bar hidden.
    /// </summary>
    public bool IsViewingOtherPlayer => requestedPlayerId is not null;

    public string MatchesText => Profile?.CareerStats.Matches.ToString(CultureInfo.InvariantCulture) ?? "0";

    public string GoalsText => Profile?.CareerStats.Goals.ToString(CultureInfo.InvariantCulture) ?? "0";

    public string AssistsText => Profile?.CareerStats.Assists.ToString(CultureInfo.InvariantCulture) ?? "0";

    public string WinsText => Profile?.CareerStats.Wins.ToString(CultureInfo.InvariantCulture) ?? "0";

    public string LossesText => Profile?.CareerStats.Losses.ToString(CultureInfo.InvariantCulture) ?? "0";

    public string AverageRatingText =>
        Profile?.CareerStats.AverageRating.ToString("0.0", CultureInfo.InvariantCulture) ?? "0.0";

    public string MvpAwardsText => Profile?.CareerStats.MvpAwards.ToString(CultureInfo.InvariantCulture) ?? "0";

    public string LikesText => Profile?.CareerStats.Likes.ToString(CultureInfo.InvariantCulture) ?? "0";

    partial void OnProfileChanged(PlayerProfileDto? value)
    {
        OnPropertyChanged(nameof(MatchesText));
        OnPropertyChanged(nameof(GoalsText));
        OnPropertyChanged(nameof(AssistsText));
        OnPropertyChanged(nameof(WinsText));
        OnPropertyChanged(nameof(LossesText));
        OnPropertyChanged(nameof(AverageRatingText));
        OnPropertyChanged(nameof(MvpAwardsText));
        OnPropertyChanged(nameof(LikesText));
    }

    partial void OnActionMessageChanged(string value) => OnPropertyChanged(nameof(HasActionMessage));

    partial void OnMyGroupsChanged(IReadOnlyList<MembershipRowItem> value) => OnPropertyChanged(nameof(MyGroupsDetail));

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        requestedPlayerId = null;

        if (query.TryGetValue(PlayerIdQueryKey, out var value)
            && Guid.TryParse(value?.ToString(), out var parsedPlayerId)
            && parsedPlayerId != Guid.Empty)
        {
            requestedPlayerId = parsedPlayerId;
        }

        OnPropertyChanged(nameof(CanEditProfile));
        OnPropertyChanged(nameof(IsViewingOtherPlayer));
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadProfileAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Refresh(CancellationToken cancellationToken)
    {
        // An explicit pull must not be answered from the cache that serves tab switches.
        responseCache.Invalidate("profile:");
        IsRefreshing = true;
        try
        {
            await LoadProfileAsync(cancellationToken);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task EditOnPickupPal(CancellationToken cancellationToken)
    {
        ActionMessage = string.Empty;

        try
        {
            if (!await externalLauncher.OpenAccountAsync(cancellationToken))
            {
                ActionMessage = ExternalLaunchError;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ActionMessage = ExternalLaunchError;
        }
    }

    [RelayCommand]
    private Task OpenLeaderboard() => navigator.OpenLeaderboardAsync();

    [RelayCommand]
    private Task OpenMyGroups() => navigator.OpenMyGroupsAsync();

    [RelayCommand]
    private Task OpenGroupMembers(AdminGroupItem? group) =>
        group is null ? Task.CompletedTask : navigator.OpenGroupMembersAsync(group.GroupChatId);

    [RelayCommand]
    private Task OpenSuperAdminGroups() => navigator.OpenSuperAdminGroupsAsync();

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    // Sign out / switch account. Only offered on the signed-in player's own profile (CanEditProfile).
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SignOut(CancellationToken cancellationToken)
    {
        var confirmed = await dialogService.ShowConfirmationAsync(
            SignOutConfirmTitle,
            SignOutConfirmMessage,
            "Sign out",
            "Cancel",
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        await authenticationCoordinator.SignOutAsync(cancellationToken);
    }

    private async Task LoadProfileAsync(CancellationToken cancellationToken)
    {
        ClearProfile();
        // Pull-to-refresh keeps the content on screen (RefreshView shows the spinner);
        // only non-content states swap to the full-page loading view.
        if (State != ViewState.Content)
        {
            State = ViewState.Loading;
        }
        IsBusy = true;

        try
        {
            var profile = requestedPlayerId is Guid playerId
                ? await profileClient.GetProfileAsync(playerId, cancellationToken)
                : await profileClient.GetCurrentProfileAsync(cancellationToken);

            if (profile is null)
            {
                ApplyNonContentState(ViewState.Empty, EmptyTitle, EmptyMessage);
                return;
            }

            Profile = profile;
            RecentForm = profile.RecentForm.Select(ProfileFormBadge.FromResult).ToArray();
            PendingNote = profile.PendingConfirmationNote ?? string.Empty;
            if (CanEditProfile)
            {
                await LoadMembershipsAsync(cancellationToken);
            }

            StateTitle = string.Empty;
            StateMessage = string.Empty;
            State = ViewState.Content;
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

    // Group membership is a secondary section: if it fails, the profile still renders and the
    // "My groups" card simply invites the player to open the membership screen, which has its own
    // error handling.
    private async Task LoadMembershipsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var memberships = await groupsClient.GetMyMembershipsAsync(cancellationToken);
            IsSuperAdmin = memberships.IsSuperAdmin;
            MyGroups = memberships.Memberships
                .Where(membership => membership.Status is GroupMembershipStatuses.Approved
                    or GroupMembershipStatuses.Pending
                    or GroupMembershipStatuses.Declined)
                .Select(membership => new MembershipRowItem(
                    membership.GroupChatId,
                    membership.GroupName,
                    membership.Status,
                    membership.Role,
                    MemberCount: null,
                    membership.RequestedAtUtc))
                .ToArray();
            AdminGroups = memberships.Memberships
                .Where(membership => membership.Status == GroupMembershipStatuses.Approved
                    && membership.Role == GroupMemberRoles.Admin)
                .Select(AdminGroupItem.FromMembership)
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            IsSuperAdmin = false;
            MyGroups = [];
            AdminGroups = [];
        }
    }

    private void ApplyNonContentState(ViewState state, string title, string message)
    {
        ClearProfile();
        StateTitle = title;
        StateMessage = message;
        State = state;
    }

    private void ClearProfile()
    {
        Profile = null;
        RecentForm = [];
        PendingNote = string.Empty;
        MyGroups = [];
        AdminGroups = [];
        IsSuperAdmin = false;
    }
}

public sealed record ProfileFormBadge(string Text, BadgeVariant Variant, string Description)
{
    public static ProfileFormBadge FromResult(MatchResult result) =>
        result switch
        {
            MatchResult.Win => new("W", BadgeVariant.Success, "Win"),
            MatchResult.Draw => new("D", BadgeVariant.Warning, "Draw"),
            MatchResult.Loss => new("L", BadgeVariant.Danger, "Loss"),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unsupported match result.")
        };
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
public partial class ProfilePageModel : IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ApplyQueryAttributes(query);
    }
}
#endif
