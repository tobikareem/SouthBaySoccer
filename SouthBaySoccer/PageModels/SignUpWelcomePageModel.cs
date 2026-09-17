using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Account created and linked. Tokens are held here until the player continues, so the welcome
/// summary is shown before the authenticated Shell replaces this stack.
/// </summary>
public partial class SignUpWelcomePageModel(IAuthenticationCoordinator authenticationCoordinator) : ObservableObject
{
    public const string Title = "Welcome";
    public const string Subtitle = "Account created and linked.";
    public const string PhoneLinkedTitle = "Phone linked to WhatsApp";
    public const string HistoryPendingTitle = "Past games still syncing";
    public const string HistoryPendingDetail = "Any games you played before today show up in a moment.";
    public const string NoGroupHint = "Not in a group yet? Ask an organizer for the group link code, or find one under Profile.";

    public const string ContinueFailedMessage = "We could not open your sessions. Tap again to retry.";

    private AuthenticationTokensResponse? _tokens;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    [ObservableProperty]
    private string _heading = string.Empty;

    [ObservableProperty]
    private string _phoneLinkedDetail = string.Empty;

    [ObservableProperty]
    private string _groupTitle = string.Empty;

    [ObservableProperty]
    private string _groupDetail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoGroup))]
    private bool _hasGroup;

    public bool HasNoGroup => !HasGroup;

    [ObservableProperty]
    private bool _historyPending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    public void Initialize(RegistrationCompletedResponse registration)
    {
        _tokens = registration.Tokens;
        Heading = $"You're in, {registration.FirstName}.";
        PhoneLinkedDetail = $"Sign in any time with {registration.PhoneMasked}.";
        HasGroup = registration.GroupNames.Count > 0;
        GroupTitle = HasGroup ? $"Joined {registration.GroupNames[0]}" : "No group yet";
        GroupDetail = HasGroup
            ? (registration.GroupNames.Count > 1
                ? $"Found from your WhatsApp groups, plus {registration.GroupNames.Count - 1} more."
                : "Found from your WhatsApp groups.")
            : NoGroupHint;
        HistoryPending = registration.HistorySyncPending;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ContinueAsync(CancellationToken cancellationToken)
    {
        if (_tokens is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await authenticationCoordinator.CompleteSignInAsync(_tokens, cancellationToken);
            _tokens = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            StatusMessage = ContinueFailedMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
