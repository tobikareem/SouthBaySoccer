using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Backs the blocking "link your group" step shown after sign-in when a player belongs to no group.
/// The player must pick one group and link it before entering the app; there is no skip.
/// </summary>
/// <summary>Navigation out of the blocking group-link step, abstracted so the page model stays testable.</summary>
public interface IGroupLinkNavigator
{
    /// <summary>Leaves the link step for the authenticated app once a group is linked.</summary>
    Task GoToAuthenticatedAppAsync();
}

public partial class LinkGroupPageModel(
    IGroupsClient groupsClient,
    IGroupLinkNavigator navigator,
    IUserDialogService dialogService) : ObservableObject
{
    public const string EmptyTitle = "No groups available";
    public const string EmptyMessage = "There are no groups to join yet. Please check back shortly.";
    public const string ErrorTitle = "Couldn't load groups";
    public const string ErrorMessage = "Something went wrong loading the groups. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load the groups you can join.";
    public const string LinkFailedTitle = "Couldn't link your group";
    public const string LinkFailedMessage = "We couldn't link you to that group. Please try again.";

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<GroupChatDto> _groups = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private GroupChatDto? _selectedGroup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isBusy;

    /// <summary>The player may continue only once a group is selected and no link is in flight.</summary>
    public bool CanContinue => SelectedGroup is not null && !IsBusy;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Appearing(CancellationToken cancellationToken) => LoadGroupsAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Refresh(CancellationToken cancellationToken) => LoadGroupsAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanContinue))]
    private async Task Continue(CancellationToken cancellationToken)
    {
        if (SelectedGroup is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await groupsClient.LinkAsync(SelectedGroup.ExternalId, cancellationToken);
            await navigator.GoToAuthenticatedAppAsync();
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

    private async Task LoadGroupsAsync(CancellationToken cancellationToken)
    {
        State = ViewState.Loading;
        IsBusy = true;
        try
        {
            var groups = await groupsClient.GetAvailableGroupsAsync(cancellationToken);
            Groups = groups;
            SelectedGroup = null;

            if (groups.Count == 0)
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
        SelectedGroup = null;
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}
