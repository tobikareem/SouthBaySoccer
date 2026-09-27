using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Members screen for one group (route <c>group-members?groupId=…</c>). Group admins approve or
/// decline pending requests and remove members; super admins additionally add a known player by
/// name search and make or unmake group admins. The server reports what the caller may do
/// (<c>CanManageMembers</c> / <c>CanAppointAdmins</c>) and enforces it; the client only shows or
/// hides the actions accordingly.
/// </summary>
public partial class GroupMembersPageModel(
    IGroupsClient groupsClient,
    IProfileNavigator navigator,
    IUserDialogService dialogService,
    IClientResponseCache responseCache) : ObservableObject
{
    public const string GroupIdQueryKey = "groupId";
    public const int MinimumSearchLength = 2;
    /// <summary>Keystrokes closer together than this collapse into one request.</summary>
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);
    public const string EmptyTitle = "No members yet";
    public const string EmptyMessage = "Nobody has joined or requested this group yet.";
    public const string ErrorTitle = "Couldn't load members";
    public const string ErrorMessage = "Something went wrong loading this group's members. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load this group's members.";
    public const string MissingGroupTitle = "Group not found";
    public const string MissingGroupMessage = "This group could not be opened. Go back and try again.";
    public const string ActionFailedMessage = "That change didn't go through. Please try again.";
    public const string SearchFailedMessage = "Search didn't work. Please try again.";
    public const string RemoveConfirmTitle = "Remove member?";

    private Guid groupChatId;
    private CancellationTokenSource? searchCancellation;

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private string _groupName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageMembers))]
    [NotifyPropertyChangedFor(nameof(CanAppointAdmins))]
    [NotifyPropertyChangedFor(nameof(ShowsAdminBadge))]
    private GroupMembersResponse? _members;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending))]
    [NotifyPropertyChangedFor(nameof(PendingCountText))]
    private IReadOnlyList<GroupMemberItem> _pending = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMembers))]
    [NotifyPropertyChangedFor(nameof(MemberCountText))]
    private IReadOnlyList<GroupMemberItem> _currentMembers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchResults))]
    private IReadOnlyList<PlayerSearchItem> _searchResults = [];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionMessage))]
    private string _actionMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The search kicked off by the latest <see cref="SearchQuery"/> change; awaitable so callers (and tests) can observe it.</summary>
    public Task PendingSearch { get; private set; } = Task.CompletedTask;

    public bool CanManageMembers => Members?.CanManageMembers ?? false;

    /// <summary>Super-admin extras (add member, make/remove admin) are shown only when the server says so.</summary>
    public bool CanAppointAdmins => Members?.CanAppointAdmins ?? false;

    /// <summary>Group admins see the role pill; super admins see the crown toggle in its place.</summary>
    public bool ShowsAdminBadge => !CanAppointAdmins;

    public bool HasPending => Pending.Count > 0;

    public bool HasMembers => CurrentMembers.Count > 0;

    public bool HasSearchResults => SearchResults.Count > 0;

    public bool HasActionMessage => !string.IsNullOrWhiteSpace(ActionMessage);

    public string MemberCountText => GroupMembershipPresentation.MemberCountText(CurrentMembers.Count);

    public string PendingCountText => Pending.Count == 1 ? "1 pending" : $"{Pending.Count} pending";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        groupChatId = query.TryGetValue(GroupIdQueryKey, out var value)
            && Guid.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : Guid.Empty;
    }

    partial void OnSearchQueryChanged(string value)
    {
        // A fresh keystroke supersedes any search still in flight.
        CancelSearch();
        ActionMessage = string.Empty;
        SearchResults = [];

        var fragment = value.Trim();
        if (fragment.Length < MinimumSearchLength)
        {
            SearchResults = [];
            return;
        }

        if (!PlayerNameSearch.IsValid(fragment))
        {
            ActionMessage = PlayerNameSearch.ValidationMessage;
            return;
        }

        searchCancellation = new CancellationTokenSource();
        PendingSearch = SearchAsync(fragment, searchCancellation.Token);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Refresh(CancellationToken cancellationToken)
    {
        responseCache.Invalidate("groups:");
        IsRefreshing = true;
        try
        {
            await LoadAsync(cancellationToken);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Approve(GroupMemberItem? member, CancellationToken cancellationToken) =>
        member is null
            ? Task.CompletedTask
            : RunActionAsync(token => groupsClient.ApproveAsync(groupChatId, member.PlayerProfileId, token), cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Decline(GroupMemberItem? member, CancellationToken cancellationToken) =>
        member is null
            ? Task.CompletedTask
            : RunActionAsync(token => groupsClient.DeclineAsync(groupChatId, member.PlayerProfileId, token), cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Remove(GroupMemberItem? member, CancellationToken cancellationToken)
    {
        if (member is null)
        {
            return;
        }

        var confirmed = await dialogService.ShowConfirmationAsync(
            RemoveConfirmTitle,
            $"{member.Name} will lose access to {GroupName}'s games until they're approved again.",
            "Remove",
            "Keep",
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        await RunActionAsync(token => groupsClient.RemoveMemberAsync(groupChatId, member.PlayerProfileId, token), cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ToggleAdmin(GroupMemberItem? member, CancellationToken cancellationToken)
    {
        if (member is null || !CanAppointAdmins)
        {
            return;
        }

        await RunActionAsync(
            token => groupsClient.SetAdminAsync(groupChatId, member.PlayerProfileId, isAdmin: !member.IsAdmin, token),
            cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task AddMember(PlayerSearchItem? player, CancellationToken cancellationToken)
    {
        if (player is null || !CanAppointAdmins)
        {
            return;
        }

        await RunActionAsync(token => groupsClient.AddMemberAsync(groupChatId, player.PlayerProfileId, token), cancellationToken);
        SearchQuery = string.Empty;
    }

    private void CancelSearch()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = null;
    }

    private async Task SearchAsync(string fragment, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounce, cancellationToken);
            var players = await groupsClient.SearchPlayersAsync(fragment, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Players already in the group (or already asking) are not offered again.
            var known = Pending.Concat(CurrentMembers).Select(member => member.PlayerProfileId).ToHashSet();
            SearchResults = players
                .Where(player => !known.Contains(player.PlayerProfileId))
                .Select(PlayerSearchItem.From)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query.
        }
        catch (Exception)
        {
            ActionMessage = SearchFailedMessage;
        }
    }

    private async Task RunActionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        ActionMessage = string.Empty;
        IsBusy = true;
        try
        {
            await action(cancellationToken);
            responseCache.Invalidate("groups:");
            await LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ActionMessage = ActionFailedMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (groupChatId == Guid.Empty)
        {
            ApplyNonContentState(ViewState.Error, MissingGroupTitle, MissingGroupMessage);
            return;
        }

        if (State != ViewState.Content)
        {
            State = ViewState.Loading;
        }

        try
        {
            var response = await groupsClient.GetMembersAsync(groupChatId, cancellationToken);
            Members = response;
            GroupName = response.GroupName;
            Pending = response.Pending.Select(GroupMemberItem.From).ToArray();
            CurrentMembers = response.Members.Select(GroupMemberItem.From).ToArray();

            if (Pending.Count == 0 && CurrentMembers.Count == 0 && !response.CanAppointAdmins)
            {
                ApplyNonContentState(ViewState.Empty, EmptyTitle, EmptyMessage);
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
        catch (HttpRequestException)
        {
            ApplyNonContentState(ViewState.Offline, OfflineTitle, OfflineMessage);
        }
        catch (Exception)
        {
            ApplyNonContentState(ViewState.Error, ErrorTitle, ErrorMessage);
        }
    }

    private void ApplyNonContentState(ViewState state, string title, string message)
    {
        // A late result from an in-flight search must not repopulate a page that has left content.
        CancelSearch();
        Pending = [];
        CurrentMembers = [];
        SearchResults = [];
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}

#if ANDROID || IOS || MACCATALYST || WINDOWS
public partial class GroupMembersPageModel : IQueryAttributable
{
    void IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query) => ApplyQueryAttributes(query);
}
#endif
