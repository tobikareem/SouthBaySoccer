using System.Net;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Announcements;
using SouthBaySoccer.Contracts.Groups;
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
    private bool loadingAudience;
    public DateTimeOffset LocalNow => timeProvider.GetLocalNow();
    public Guid InitialGroupId { get; set; }

    [ObservableProperty] private ViewState _state = ViewState.Loading;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _stateTitle = string.Empty;
    [ObservableProperty] private string _stateMessage = string.Empty;
    [ObservableProperty] private IReadOnlyList<GroupMembershipDto> _groups = [];
    [ObservableProperty] private GroupMembershipDto? _group;
    [ObservableProperty] private string _body = string.Empty;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private bool _isSent;
    [ObservableProperty] private string _inlineError = string.Empty;
    [ObservableProperty] private IReadOnlyList<SentAnnouncementDto> _recentlySent = [];

    public int CharacterCount => Body.Length;
    public string CharacterCountLabel => $"{CharacterCount} / {MaximumBodyLength}";
    public string GroupName => Group?.GroupName ?? string.Empty;
    public string PreviewGroupName => Group?.GroupName ?? string.Empty;
    public string PreviewBody => string.IsNullOrEmpty(Body) ? "Your announcement preview appears here." : Body;
    public string BroadcastLabel => "Post announcement";
    public bool IsComposerEnabled => !IsSent && !IsSending;
    public bool CanSend => IsComposerEnabled
        && Group is not null
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

    partial void OnGroupChanged(GroupMembershipDto? value)
    {
        if (!loadingAudience)
        {
            ResetIdempotencyWhenCompositionChanges();
        }
        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(PreviewGroupName));
        OnPropertyChanged(nameof(BroadcastLabel));
        SendCommand.NotifyCanExecuteChanged();
    }

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
            var selectedId = Group?.GroupChatId ?? InitialGroupId;
            var memberships = await groupsClient.GetMyMembershipsAsync(cancellationToken);
            // Replacing Picker items can briefly clear its two-way selection. Preserve the retry key
            // until the final authorized audience has been restored.
            loadingAudience = true;
            Groups = memberships.Memberships
                .Where(item => item.Status == GroupMembershipStatuses.Approved
                    && (item.Role == GroupMemberRoles.Admin || memberships.IsSuperAdmin))
                .ToArray();
            Group = Groups.FirstOrDefault(item => item.GroupChatId == selectedId) ?? Groups.FirstOrDefault();
            loadingAudience = false;
            ResetIdempotencyWhenCompositionChanges();
            RecentlySent = (await announcementsClient.GetSentAsync(10, cancellationToken)).Announcements;
            State = Group is null ? ViewState.Empty : ViewState.Content;
            StateTitle = Group is null ? "No groups to manage" : string.Empty;
            StateMessage = Group is null ? "Only admins can post announcements to their groups." : string.Empty;
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
            loadingAudience = false;
            IsRefreshing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send(CancellationToken cancellationToken)
    {
        if (!CanSend || Group is null)
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
                Group.GroupChatId,
                new PostAnnouncementRequest(Body),
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
        new(Group?.GroupChatId ?? Guid.Empty, Body);

    private void ResetIdempotencyWhenCompositionChanges()
    {
        if (attemptedComposition is not null && attemptedComposition != CurrentComposition())
        {
            idempotencyKey = Guid.NewGuid().ToString("N");
            attemptedComposition = null;
        }
    }

    private sealed record BroadcastComposition(Guid GroupId, string Body);
}
