using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>Navigation out of the blocking group-join step, abstracted so the page model stays testable.</summary>
public interface IGroupLinkNavigator
{
    /// <summary>Leaves the join step for the authenticated app once the requests are in.</summary>
    Task GoToAuthenticatedAppAsync();
}

/// <summary>
/// Backs the "join your groups" step shown after sign-in when a player belongs to no group, and
/// after every new sign-up. The player picks every group they play with (multi-select) and submits
/// one request; the server approves each group instantly when Pickup Pal already lists the player
/// in that WhatsApp group and otherwise leaves it Pending for a group admin. The outcome is shown
/// before continuing. A player already in a group may skip without requesting more.
/// </summary>
public partial class LinkGroupPageModel : ObservableObject
{
    public const string EmptyTitle = "No groups available";
    public const string EmptyMessage = "There are no groups to join yet. Please check back shortly.";
    public const string ErrorTitle = "Couldn't load groups";
    public const string ErrorMessage = "Something went wrong loading the groups. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load the groups you can join.";
    public const string LinkFailedTitle = "Couldn't send your requests";
    public const string LinkFailedMessage = "We couldn't send your group requests. Please try again.";
    public const string AllApprovedTitle = "You're in";
    public const string SomeApprovedTitle = "Requests sent";
    public const string AllPendingTitle = "Waiting for approval";

    private readonly IGroupsClient groupsClient;
    private readonly IGroupLinkNavigator navigator;
    private readonly IUserDialogService dialogService;
    private readonly IClientResponseCache responseCache;

    public LinkGroupPageModel(
        IGroupsClient groupsClient,
        IGroupLinkNavigator navigator,
        IUserDialogService dialogService,
        IClientResponseCache responseCache)
    {
        this.groupsClient = groupsClient;
        this.navigator = navigator;
        this.dialogService = dialogService;
        this.responseCache = responseCache;
        SelectedGroups.CollectionChanged += OnSelectedGroupsChanged;
    }

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    /// <summary>Groups the player can request; already-approved and already-pending groups are excluded.</summary>
    [ObservableProperty]
    private IReadOnlyList<GroupChoiceItem> _groups = [];

    /// <summary>Requests already awaiting an admin from an earlier visit, shown above the choices.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlreadyPending))]
    [NotifyPropertyChangedFor(nameof(HasExistingMembership))]
    [NotifyPropertyChangedFor(nameof(CanSkip))]
    [NotifyCanExecuteChangedFor(nameof(SkipCommand))]
    private IReadOnlyList<GroupRequestOutcome> _alreadyPending = [];

    /// <summary>Groups the player is already an approved member of (e.g. auto-approved from WhatsApp at sign-up).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlreadyJoined))]
    [NotifyPropertyChangedFor(nameof(HasExistingMembership))]
    [NotifyPropertyChangedFor(nameof(CanSkip))]
    [NotifyCanExecuteChangedFor(nameof(SkipCommand))]
    private IReadOnlyList<GroupRequestOutcome> _alreadyJoined = [];

    /// <summary>What the server decided for each requested group; non-empty once a submit succeeded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(IsChoosing))]
    [NotifyPropertyChangedFor(nameof(ResultTitle))]
    [NotifyPropertyChangedFor(nameof(ResultSubtitle))]
    [NotifyPropertyChangedFor(nameof(HasApprovedGroup))]
    private IReadOnlyList<GroupRequestOutcome> _outcomes = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    [NotifyPropertyChangedFor(nameof(CanSkip))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipCommand))]
    private bool _isBusy;

    /// <summary>
    /// Bound to the CollectionView's SelectedItems (SelectionMode=Multiple), which is why it is an
    /// <c>ObservableCollection&lt;object&gt;</c>; the items are always <see cref="GroupChoiceItem"/>.
    /// </summary>
    public ObservableCollection<object> SelectedGroups { get; } = [];

    public int SelectedCount => SelectedGroups.Count;

    public string SelectedCountText => SelectedCount == 1 ? "1 selected" : $"{SelectedCount} selected";

    /// <summary>The player may continue only once at least one group is selected and no request is in flight.</summary>
    public bool CanContinue => SelectedCount > 0 && !IsBusy;

    public bool HasAlreadyPending => AlreadyPending.Count > 0;

    public bool HasAlreadyJoined => AlreadyJoined.Count > 0;

    /// <summary>The player already has an approved or pending membership from an earlier step.</summary>
    public bool HasExistingMembership => HasAlreadyJoined || HasAlreadyPending;

    /// <summary>
    /// A player who already belongs to (or awaits) a group may leave without requesting more; a
    /// player with no membership at all must pick one, since RSVP is limited to group members.
    /// </summary>
    public bool CanSkip => HasExistingMembership && !IsBusy;

    public bool HasResult => Outcomes.Count > 0;

    /// <summary>The selection step is on screen until a submit succeeds.</summary>
    public bool IsChoosing => !HasResult;

    public bool HasApprovedGroup => Outcomes.Any(outcome => outcome.IsApproved);

    public string ResultTitle
    {
        get
        {
            if (!HasResult)
            {
                return string.Empty;
            }

            var approved = Outcomes.Count(outcome => outcome.IsApproved);
            return approved == Outcomes.Count ? AllApprovedTitle
                : approved == 0 ? AllPendingTitle
                : SomeApprovedTitle;
        }
    }

    public string ResultSubtitle
    {
        get
        {
            if (!HasResult)
            {
                return string.Empty;
            }

            var approved = Outcomes.Count(outcome => outcome.IsApproved);
            var pending = Outcomes.Count - approved;
            return (approved, pending) switch
            {
                (_, 0) => approved == 1 ? "Joined 1 group." : $"Joined {approved} groups.",
                (0, _) => pending == 1 ? "1 request awaiting a group admin." : $"{pending} requests awaiting a group admin.",
                _ => $"{approved} joined · {pending} awaiting approval",
            };
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadGroupsAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Refresh(CancellationToken cancellationToken)
    {
        // An explicit pull must not be answered from the cache that serves tab switches.
        responseCache.Invalidate("groups:");
        return LoadGroupsAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanContinue))]
    private async Task Continue(CancellationToken cancellationToken)
    {
        var requested = SelectedGroups.OfType<GroupChoiceItem>().ToArray();
        if (requested.Length == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var ids = requested.Select(item => item.Id).ToArray();
            var response = await groupsClient.RequestMembershipsAsync(ids, cancellationToken);
            var byGroup = LatestByGroup(response.Memberships);
            Outcomes = requested
                .Select(item => new GroupRequestOutcome(
                    item.Id,
                    item.Name,
                    byGroup.TryGetValue(item.Id, out var membership) ? membership.Status : GroupMembershipStatuses.Pending))
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await dialogService.ShowAlertAsync(LinkFailedTitle, LinkFailedMessage, "OK", cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Always available after a submit: a player whose requests are all Pending still enters the app
    // (games of groups they are not in are view-only) rather than being trapped on this step.
    [RelayCommand]
    private Task ContinueToApp() => navigator.GoToAuthenticatedAppAsync();

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanSkip))]
    private Task Skip() => navigator.GoToAuthenticatedAppAsync();

    /// <summary>The contract does not promise one row per group (e.g. a Declined history row plus a new Pending one); the last row wins.</summary>
    internal static Dictionary<Guid, GroupMembershipDto> LatestByGroup(IReadOnlyList<GroupMembershipDto> memberships) =>
        memberships
            .GroupBy(membership => membership.GroupChatId)
            .ToDictionary(group => group.Key, group => group.Last());

    private void OnSelectedGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<GroupChoiceItem>())
            {
                item.IsSelected = false;
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in Groups)
            {
                item.IsSelected = SelectedGroups.Contains(item);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<GroupChoiceItem>())
            {
                item.IsSelected = true;
            }
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(CanContinue));
        ContinueCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadGroupsAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        IsBusy = true;
        try
        {
            var catalog = await groupsClient.GetCatalogAsync(cancellationToken);
            SelectedGroups.Clear();
            Outcomes = [];
            AlreadyPending = catalog
                .Where(group => group.MembershipStatus == GroupMembershipStatuses.Pending)
                .Select(group => new GroupRequestOutcome(group.Id, group.GroupName, group.MembershipStatus))
                .ToArray();
            AlreadyJoined = catalog
                .Where(group => group.MembershipStatus == GroupMembershipStatuses.Approved)
                .Select(group => new GroupRequestOutcome(group.Id, group.GroupName, group.MembershipStatus))
                .ToArray();
            Groups = catalog
                .Where(group => group.MembershipStatus is not (GroupMembershipStatuses.Approved or GroupMembershipStatuses.Pending))
                .Select(group => new GroupChoiceItem(group))
                .ToArray();

            if (Groups.Count == 0 && AlreadyPending.Count == 0 && AlreadyJoined.Count == 0)
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
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyNonContentState(ViewState state, string title, string message)
    {
        Groups = [];
        AlreadyPending = [];
        AlreadyJoined = [];
        SelectedGroups.Clear();
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}
