using System.Net;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.UserActivity;
using ViewState = SouthBaySoccer.Controls.ViewState;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.PageModels;

public partial class UserActivityPageModel(IUserActivityClient client, IProfileNavigator navigator) : ObservableObject
{
    public const string TrackingNotice = "Tracking begins with this release. Earlier sign-ups and sign-ins are not included.";
    private int _page;
    private bool _loading;

    [ObservableProperty] private ViewState _state = ViewState.Loading;
    [ObservableProperty] private string _stateTitle = string.Empty;
    [ObservableProperty] private string _stateMessage = string.Empty;
    [ObservableProperty] private string _pagingError = string.Empty;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _trackingText = TrackingNotice;
    public ObservableCollection<UserActivityItem> Items { get; } = [];

    [RelayCommand] private Task Back() => navigator.GoBackAsync();
    [RelayCommand] private Task Appearing(CancellationToken cancellationToken) => LoadAsync(true, cancellationToken);
    [RelayCommand] private Task Refresh(CancellationToken cancellationToken) => LoadAsync(true, cancellationToken);
    [RelayCommand] private Task LoadMore(CancellationToken cancellationToken) =>
        HasMore ? LoadAsync(false, cancellationToken) : Task.CompletedTask;

    private async Task LoadAsync(bool refresh, CancellationToken cancellationToken)
    {
        // All commands share this gate so refresh and paging cannot interleave their results.
        if (_loading) return;
        _loading = true;
        IsBusy = true;
        IsRefreshing = refresh;
        PagingError = string.Empty;
        if (Items.Count == 0) State = ViewState.Loading;
        try
        {
            var next = refresh ? 1 : _page + 1;
            var response = await client.GetAsync(next, 25, cancellationToken);
            var rows = response.Items.Select(entry => new UserActivityItem(entry));
            if (refresh) Items.Clear();
            var existingIds = Items.Select(row => row.Id).ToHashSet();
            foreach (var row in rows)
            {
                if (existingIds.Add(row.Id)) Items.Add(row);
            }
            _page = next;
            HasMore = response.HasMore;
            TrackingText = response.TrackingStartedAtUtc is { } start
                ? $"Recorded activity since {UserActivityItem.LocalTime(start)}. Earlier history is not included."
                : TrackingNotice;
            StateTitle = Items.Count == 0 ? "No recorded activity yet" : string.Empty;
            StateMessage = Items.Count == 0 ? TrackingNotice + " Refresh to check for new activity." : string.Empty;
            State = Items.Count == 0 ? ViewState.Empty : ViewState.Content;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            Items.Clear();
            HasMore = false;
            _page = 0;
            TrackingText = TrackingNotice;
            StateTitle = "Access denied";
            StateMessage = "User activity is available to the owner only.";
            State = ViewState.Error;
        }
        catch (Exception exception)
        {
            var offline = exception is HttpRequestException { StatusCode: null };
            var message = offline ? "Reconnect and try again." : "Couldn't load activity. Please try again.";
            if (Items.Count > 0)
            {
                PagingError = message;
                State = ViewState.Content;
            }
            else
            {
                StateTitle = offline ? "You're offline" : "Couldn't load activity";
                StateMessage = message;
                State = offline ? ViewState.Offline : ViewState.Error;
            }
        }
        finally
        {
            _loading = false;
            IsBusy = false;
            IsRefreshing = false;
        }
    }
}

public sealed class UserActivityItem(UserActivityEntryDto entry)
{
    public Guid Id => entry.Id;
    public string DisplayName => entry.DisplayName;
    public string ActivityText => $"{(entry.ActivityType switch { "SignUp" => "Signed up", "SignIn" => "Signed in", _ => "Activity" })} · {LocalTime(entry.OccurredAtUtc)}";
    public string FirstRecordedText => $"First recorded: {LocalTime(entry.FirstRecordedActivityAtUtc)}";
    public string LastRecordedText => $"Last recorded: {LocalTime(entry.LastRecordedActivityAtUtc)}";
    public string SignInCountText => $"Recorded sign-ins: {entry.SignInCount}";
    public string GroupsText => entry.Groups.Count == 0 ? "Current groups: None" :
        "Current groups: " + string.Join("; ", entry.Groups.Select(group => $"{group.GroupName} · {group.Status}"));
    public static string LocalTime(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("g");
}
