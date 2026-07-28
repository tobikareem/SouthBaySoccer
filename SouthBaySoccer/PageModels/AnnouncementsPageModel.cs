using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

public partial class AnnouncementsPageModel(
    IAnnouncementsClient announcementsClient,
    IAnnouncementsNavigator navigator,
    IClientResponseCache responseCache,
    TimeProvider timeProvider) : ObservableObject
{
    public const string ErrorTitle = "Couldn't load announcements";
    public const string ErrorMessage = "Something went wrong loading announcements. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load group announcements.";

    private readonly List<AnnouncementItemViewModel> loadedItems = [];
    private DateTime? nextCursorUtc;
    private Guid? nextCursorId;

    public Guid GroupId { get; set; }

    [ObservableProperty] private ViewState _state = ViewState.Loading;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _isLoadingMore;
    [ObservableProperty] private bool _isMarkingRead;
    [ObservableProperty] private string _loadMoreError = string.Empty;
    [ObservableProperty] private string _stateTitle = string.Empty;
    [ObservableProperty] private string _stateMessage = string.Empty;
    [ObservableProperty] private string _groupName = string.Empty;
    [ObservableProperty] private bool _showUnreadOnly;
    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private IReadOnlyList<AnnouncementDayGroup> _dayGroups = [];

    public string UnreadTabLabel => $"Unread · {UnreadCount}";
    public IReadOnlyList<string> FilterLabels => ["All", UnreadTabLabel];
    public bool HasMore => nextCursorUtc is not null && nextCursorId is not null;
    public bool HasLoadMoreError => !string.IsNullOrWhiteSpace(LoadMoreError);
    public bool CanMarkAllRead => UnreadCount > 0 && !IsMarkingRead;

    partial void OnLoadMoreErrorChanged(string value) => OnPropertyChanged(nameof(HasLoadMoreError));
    partial void OnIsMarkingReadChanged(bool value)
    {
        OnPropertyChanged(nameof(CanMarkAllRead));
        MarkAllReadCommand.NotifyCanExecuteChanged();
    }

    partial void OnShowUnreadOnlyChanged(bool value) => RebuildView();
    partial void OnUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(UnreadTabLabel));
        OnPropertyChanged(nameof(FilterLabels));
        OnPropertyChanged(nameof(CanMarkAllRead));
        MarkAllReadCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Appearing(CancellationToken cancellationToken)
    {
        if (GroupId == Guid.Empty || loadedItems.Count > 0)
        {
            return;
        }

        await LoadAsync(replace: true, cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task Refresh(CancellationToken cancellationToken)
    {
        // Without this the feed's first page is served from its own 60s cache, so pulling to
        // refresh could return the identical list and look like nothing had happened.
        responseCache.Invalidate("announcements:");
        return LoadAsync(replace: true, cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadMore(CancellationToken cancellationToken)
    {
        if (!HasMore || IsLoadingMore || HasLoadMoreError)
        {
            return;
        }

        IsLoadingMore = true;
        try
        {
            await LoadAsync(replace: false, cancellationToken);
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RetryLoadMore(CancellationToken cancellationToken)
    {
        LoadMoreError = string.Empty;
        await LoadMore(cancellationToken);
    }

    [RelayCommand]
    private void ShowAll() => ShowUnreadOnly = false;

    [RelayCommand]
    private void ShowUnread() => ShowUnreadOnly = true;

    [RelayCommand]
    private void ApplyFilter(string filter) => ShowUnreadOnly = !filter.Equals("All", StringComparison.Ordinal);

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanMarkAllRead))]
    private async Task MarkAllRead(CancellationToken cancellationToken)
    {
        if (UnreadCount == 0)
        {
            return;
        }

        IsMarkingRead = true;
        try
        {
            var response = await announcementsClient.MarkReadAsync(GroupId, cancellationToken);
            foreach (var item in loadedItems)
            {
                item.IsUnread = false;
            }

            UnreadCount = response.UnreadCount;
            RebuildView();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            ApplyError(ViewState.Offline, OfflineTitle, OfflineMessage);
        }
        catch (Exception)
        {
            ApplyError(ViewState.Error, ErrorTitle, ErrorMessage);
        }
        finally
        {
            IsMarkingRead = false;
        }
    }

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    private async Task LoadAsync(bool replace, CancellationToken cancellationToken)
    {
        if (replace)
        {
            // Only blank the screen for a genuine first load. During pull-to-refresh the list must
            // stay on screen — swapping it for a spinner while RefreshView is already showing its
            // own makes the content flash and the gesture feel broken.
            if (loadedItems.Count == 0)
            {
                State = ViewState.Loading;
            }

            IsRefreshing = true;
        }

        try
        {
            LoadMoreError = string.Empty;
            var response = await announcementsClient.GetFeedAsync(
                GroupId,
                20,
                replace ? null : nextCursorUtc,
                replace ? null : nextCursorId,
                cancellationToken);
            if (replace)
            {
                loadedItems.Clear();
            }

            GroupName = response.GroupName;
            UnreadCount = response.UnreadCount;
            nextCursorUtc = response.NextCursorUtc;
            nextCursorId = response.NextCursorId;
            loadedItems.AddRange(response.Announcements.Select(Map));
            RebuildView();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            if (replace)
            {
                ApplyError(ViewState.Offline, OfflineTitle, OfflineMessage);
            }
            else
            {
                LoadMoreError = "Couldn't load more announcements. Check your connection and retry.";
            }
        }
        catch (Exception)
        {
            if (replace)
            {
                ApplyError(ViewState.Error, ErrorTitle, ErrorMessage);
            }
            else
            {
                LoadMoreError = "Couldn't load more announcements. Please retry.";
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private AnnouncementItemViewModel Map(SouthBaySoccer.Contracts.Announcements.AnnouncementDto dto)
    {
        var local = ToLocal(dto.SentAtUtc);
        return new AnnouncementItemViewModel(dto) { TimeLabel = FormatTimeLabel(local) };
    }

    private DateTimeOffset ToLocal(DateTime sentAtUtc) =>
        TimeZoneInfo.ConvertTime(
            new DateTimeOffset(DateTime.SpecifyKind(sentAtUtc, DateTimeKind.Utc)),
            timeProvider.LocalTimeZone);

    /// <summary>
    /// A bare clock time only reads correctly for today. Anything older needs its day back, or
    /// every announcement in the "Earlier" group claims to have arrived this afternoon.
    /// </summary>
    private string FormatTimeLabel(DateTimeOffset local)
    {
        var today = timeProvider.GetLocalNow().Date;
        var age = today - local.Date;

        return age.Days switch
        {
            0 => local.ToString("h:mm tt"),
            < 7 and > 0 => local.ToString("ddd h:mm tt"),
            _ => local.ToString("MMM d"),
        };
    }

    private void RebuildView()
    {
        var nowLocal = timeProvider.GetLocalNow();
        var visible = ShowUnreadOnly ? loadedItems.Where(item => item.IsUnread) : loadedItems;
        DayGroups = visible
            .GroupBy(item =>
            {
                var localDate = TimeZoneInfo.ConvertTime(
                    new DateTimeOffset(DateTime.SpecifyKind(item.SentAtUtc, DateTimeKind.Utc)),
                    timeProvider.LocalTimeZone).Date;
                return localDate == nowLocal.Date ? "Today" : "Earlier";
            })
            .OrderBy(group => group.Key == "Today" ? 0 : 1)
            .Select(group => new AnnouncementDayGroup(group.Key, group.ToArray()))
            .ToArray();

        State = DayGroups.Count == 0 ? ViewState.Empty : ViewState.Content;
        StateTitle = DayGroups.Count == 0 ? "You're all caught up." : string.Empty;
        StateMessage = string.Empty;
    }

    private void ApplyError(ViewState state, string title, string message)
    {
        State = state;
        StateTitle = title;
        StateMessage = message;
    }
}
