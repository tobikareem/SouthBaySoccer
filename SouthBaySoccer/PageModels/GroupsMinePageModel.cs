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
/// Profile → "My groups": the player's memberships with their status, a Leave/Cancel action, and
/// the remaining groups they can still request. The catalogue already carries the caller's own
/// status per group, so one read builds both lists.
/// </summary>
public partial class GroupsMinePageModel(
    IGroupsClient groupsClient,
    IProfileNavigator navigator,
    IUserDialogService dialogService,
    IClientResponseCache responseCache) : ObservableObject
{
    public const string EmptyTitle = "No groups yet";
    public const string EmptyMessage = "There are no groups to join yet. Please check back shortly.";
    public const string ErrorTitle = "Couldn't load your groups";
    public const string ErrorMessage = "Something went wrong loading your groups. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load your groups.";
    public const string RequestFailedMessage = "We couldn't send that request. Please try again.";
    public const string LeaveFailedMessage = "We couldn't update that membership. Please try again.";
    public const string LeaveConfirmTitle = "Leave group?";
    public const string CancelConfirmTitle = "Cancel request?";

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemberships))]
    [NotifyPropertyChangedFor(nameof(HasNoMemberships))]
    private IReadOnlyList<MembershipRowItem> _memberships = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJoinableGroups))]
    private IReadOnlyList<JoinableGroupItem> _joinableGroups = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionMessage))]
    private string _actionMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public bool HasMemberships => Memberships.Count > 0;

    public bool HasNoMemberships => !HasMemberships;

    public bool HasJoinableGroups => JoinableGroups.Count > 0;

    public bool HasActionMessage => !string.IsNullOrWhiteSpace(ActionMessage);

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
    private Task Request(JoinableGroupItem? group, CancellationToken cancellationToken) =>
        group is null
            ? Task.CompletedTask
            : RunActionAsync(
                token => groupsClient.RequestMembershipsAsync([group.GroupChatId], token),
                RequestFailedMessage,
                cancellationToken);

    /// <summary>A declined request may be sent again; the server decides whether to accept it.</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task RequestAgain(MembershipRowItem? membership, CancellationToken cancellationToken) =>
        membership is null
            ? Task.CompletedTask
            : RunActionAsync(
                token => groupsClient.RequestMembershipsAsync([membership.GroupChatId], token),
                RequestFailedMessage,
                cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Leave(MembershipRowItem? membership, CancellationToken cancellationToken)
    {
        if (membership is null)
        {
            return;
        }

        var confirmed = await dialogService.ShowConfirmationAsync(
            membership.IsPending ? CancelConfirmTitle : LeaveConfirmTitle,
            membership.IsPending
                ? $"Withdraw your request to join {membership.Name}?"
                : $"You'll leave {membership.Name}. Its games become view-only until you're approved again.",
            membership.IsPending ? "Cancel request" : "Leave",
            "Keep",
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        await RunActionAsync(
            token => groupsClient.LeaveAsync(membership.GroupChatId, token),
            LeaveFailedMessage,
            cancellationToken);
    }

    private async Task RunActionAsync(
        Func<CancellationToken, Task> action,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        ActionMessage = string.Empty;
        IsBusy = true;
        try
        {
            await action(cancellationToken);
            // Every membership write changes what the catalogue reports, so drop any cached copy
            // (the API decorator does this too; Seed mode has no decorator) and re-read.
            responseCache.Invalidate("groups:");
            await LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Recoverable: the lists stay on screen and the player can retry the action.
            ActionMessage = failureMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (State != ViewState.Content)
        {
            State = ViewState.Loading;
        }

        try
        {
            // The catalogue carries status + member counts; memberships carry the request dates.
            var catalogTask = groupsClient.GetCatalogAsync(cancellationToken);
            var membershipsTask = groupsClient.GetMyMembershipsAsync(cancellationToken);
            await Task.WhenAll(catalogTask, membershipsTask);
            var catalog = await catalogTask;
            var byGroup = LinkGroupPageModel.LatestByGroup((await membershipsTask).Memberships);

            Memberships = catalog
                .Where(group => group.MembershipStatus is GroupMembershipStatuses.Approved
                    or GroupMembershipStatuses.Pending
                    or GroupMembershipStatuses.Declined)
                .Select(group => MembershipRowItem.From(group, byGroup.GetValueOrDefault(group.Id)))
                .ToArray();
            JoinableGroups = catalog
                .Where(group => group.MembershipStatus is GroupMembershipStatuses.None
                    or GroupMembershipStatuses.Removed or GroupMembershipStatuses.Withdrawn)
                .Select(group => new JoinableGroupItem(group.Id, group.GroupName, group.MemberCount))
                .ToArray();

            if (catalog.Count == 0)
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
        Memberships = [];
        JoinableGroups = [];
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}
