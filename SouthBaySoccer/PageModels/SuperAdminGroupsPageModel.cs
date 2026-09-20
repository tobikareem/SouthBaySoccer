using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Super admin "Groups &amp; admins" (route <c>super-admin-groups</c>): every group with live member
/// and pending counts; tapping one opens its Members screen with the super-admin extras. Reachable
/// only from the Profile card the server unlocks via <c>IsSuperAdmin</c>; the server also rejects
/// the privileged writes for anyone else.
/// </summary>
public partial class SuperAdminGroupsPageModel(
    IGroupsClient groupsClient,
    IProfileNavigator navigator,
    IClientResponseCache responseCache) : ObservableObject
{
    public const string EmptyTitle = "No groups yet";
    public const string EmptyMessage = "No WhatsApp groups have been imported yet.";
    public const string ErrorTitle = "Couldn't load groups";
    public const string ErrorMessage = "Something went wrong loading the groups. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load the groups.";

    [ObservableProperty]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _stateTitle = string.Empty;

    [ObservableProperty]
    private string _stateMessage = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<AdminGroupItem> _groups = [];

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

    [RelayCommand]
    private Task OpenGroup(AdminGroupItem? group) =>
        group is null ? Task.CompletedTask : navigator.OpenGroupMembersAsync(group.GroupChatId);

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (State != ViewState.Content)
        {
            State = ViewState.Loading;
        }

        try
        {
            var catalog = await groupsClient.GetCatalogAsync(cancellationToken);
            Groups = catalog.Select(AdminGroupItem.FromCatalog).ToArray();

            if (Groups.Count == 0)
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
        Groups = [];
        StateTitle = title;
        StateMessage = message;
        State = state;
    }
}
