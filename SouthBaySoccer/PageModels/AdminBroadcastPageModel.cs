using System.Net;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Announcements;
using SouthBaySoccer.Services.Clients;
using ViewState = SouthBaySoccer.Controls.ViewState;

namespace SouthBaySoccer.PageModels;

public partial class AdminBroadcastPageModel(
    IGroupsClient groupsClient,
    IAnnouncementsClient announcementsClient,
    IAnnouncementsNavigator navigator,
    TimeProvider timeProvider) : ObservableObject
{
    public const int MaximumBodyLength = 500;
    public const string ErrorTitle = "Couldn't load broadcasts";
    public const string ErrorMessage = "Something went wrong loading the broadcast composer. Please try again.";
    public const string OfflineTitle = "You're offline";
    public const string OfflineMessage = "Reconnect to load groups and send a broadcast.";

    private string idempotencyKey = Guid.NewGuid().ToString("N");
    private BroadcastComposition? attemptedComposition;
    public DateTimeOffset LocalNow => timeProvider.GetLocalNow();

    [ObservableProperty] private ViewState _state = ViewState.Loading;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _stateTitle = string.Empty;
    [ObservableProperty] private string _stateMessage = string.Empty;
    [ObservableProperty] private IReadOnlyList<GroupChoiceViewModel> _groups = [];
    [ObservableProperty] private GroupChoiceViewModel? _selectedGroup;
    [ObservableProperty] private string _body = string.Empty;
    [ObservableProperty] private bool _sendPush = true;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private bool _isSent;
    [ObservableProperty] private string _inlineError = string.Empty;
    [ObservableProperty] private IReadOnlyList<SentAnnouncementDto> _recentlySent = [];

    public int CharacterCount => Body.Length;
    public string CharacterCountLabel => $"{CharacterCount} / {MaximumBodyLength}";
    public string PreviewGroupName => SelectedGroup?.GroupName ?? string.Empty;
    public string PreviewBody => string.IsNullOrEmpty(Body) ? "Your announcement preview appears here." : Body;
    public string PushTitle => SelectedGroup is null ? "N9ja Bay" : $"N9ja Bay · {SelectedGroup.GroupName}";
    public string BroadcastLabel => $"Broadcast to {SelectedGroup?.MemberCount ?? 0} members";
    public bool IsComposerEnabled => !IsSent && !IsSending;
    public bool CanSend => IsComposerEnabled
        && SelectedGroup is not null
        && !string.IsNullOrWhiteSpace(Body)
        && Body.Length <= MaximumBodyLength;

    partial void OnBodyChanged(string value)
    {
        ResetIdempotencyWhenCompositionChanges();
        InlineError = value.Length > MaximumBodyLength
            ? $"Keep the message to {MaximumBodyLength} characters or fewer."
            : string.Empty;
        OnPropertyChanged(nameof(CharacterCount));
        OnPropertyChanged(nameof(CharacterCountLabel));
        OnPropertyChanged(nameof(PreviewBody));
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedGroupChanged(GroupChoiceViewModel? value)
    {
        ResetIdempotencyWhenCompositionChanges();
        foreach (var group in Groups)
        {
            group.IsSelected = ReferenceEquals(group, value);
        }

        OnPropertyChanged(nameof(PreviewGroupName));
        OnPropertyChanged(nameof(PushTitle));
        OnPropertyChanged(nameof(BroadcastLabel));
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnSendPushChanged(bool value) => ResetIdempotencyWhenCompositionChanges();

    partial void OnIsSendingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsComposerEnabled));
        OnPropertyChanged(nameof(CanSend));
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSentChanged(bool value)
    {
        OnPropertyChanged(nameof(IsComposerEnabled));
        OnPropertyChanged(nameof(CanSend));
        SendCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task Appearing(CancellationToken cancellationToken)
    {
        if (State == ViewState.Content)
        {
            return;
        }

        State = ViewState.Loading;
        IsRefreshing = true;
        try
        {
            var groupsResponse = await groupsClient.GetMyGroupsAsync(cancellationToken);
            var groups = groupsResponse.Groups
                .Select(group => new GroupChoiceViewModel(group))
                .ToArray();
            Groups = groups;
            SelectedGroup = groups.FirstOrDefault(item => item.Group.IsPrimary) ?? groups.FirstOrDefault();
            RecentlySent = (await announcementsClient.GetSentAsync(10, cancellationToken)).Announcements;
            State = groups.Length == 0 ? ViewState.Empty : ViewState.Content;
            StateTitle = groups.Length == 0 ? "No admin groups" : string.Empty;
            StateMessage = groups.Length == 0 ? "You need an admin group before you can broadcast." : string.Empty;
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
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private void SelectGroup(GroupChoiceViewModel group)
    {
        if (IsComposerEnabled && Groups.Contains(group))
        {
            SelectedGroup = group;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send(CancellationToken cancellationToken)
    {
        if (!CanSend || SelectedGroup is null)
        {
            InlineError = string.IsNullOrWhiteSpace(Body) ? "Enter a message before broadcasting." : InlineError;
            return;
        }

        IsSending = true;
        InlineError = string.Empty;
        try
        {
            attemptedComposition ??= CurrentComposition();
            var sent = await announcementsClient.PostAsync(
                SelectedGroup.Id,
                new PostAnnouncementRequest(Body, SendPush),
                idempotencyKey,
                cancellationToken);
            RecentlySent = [sent, .. RecentlySent];
            IsSent = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiRequestException exception) when (exception.StatusCode == HttpStatusCode.BadRequest)
        {
            InlineError = exception.UserMessage;
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
            IsSending = false;
        }
    }

    [RelayCommand]
    private void Reset()
    {
        Body = string.Empty;
        SendPush = true;
        IsSent = false;
        InlineError = string.Empty;
        idempotencyKey = Guid.NewGuid().ToString("N");
        attemptedComposition = null;
    }

    [RelayCommand]
    private Task Back() => navigator.GoBackAsync();

    private void ApplyError(ViewState state, string title, string message)
    {
        State = state;
        StateTitle = title;
        StateMessage = message;
    }

    private BroadcastComposition CurrentComposition() =>
        new(SelectedGroup?.Id ?? Guid.Empty, Body, SendPush);

    private void ResetIdempotencyWhenCompositionChanges()
    {
        if (attemptedComposition is not null && attemptedComposition != CurrentComposition())
        {
            idempotencyKey = Guid.NewGuid().ToString("N");
            attemptedComposition = null;
        }
    }

    private sealed record BroadcastComposition(Guid GroupId, string Body, bool SendPush);
}
