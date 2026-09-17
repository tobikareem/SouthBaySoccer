using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.PageModels;

/// <summary>Sign-in step 2: the phone lookup found an account; prove possession with !!login.</summary>
public partial class SignInVerifyPageModel(
    IOnboardingFlow flow,
    IOnboardingNavigator navigator,
    PickupPalOptions options) : ObservableObject
{
    public const string StepLabel = "Sign in";
    public const string Title = "Verify it's you";
    public const string Heading = "One tap on WhatsApp to finish.";
    public const string Description =
        "We found your account. To make sure it's really you, send the bot a message from this number and tap the link it sends back.";
    public const string RememberTitle = "Remember this device";
    public const string RememberDetail = "Skip this step here for 30 days.";
    public const string WhyHeading = "Why the extra step?";
    public const string WhyMessage =
        "Knowing a phone number isn't the same as owning it. The WhatsApp reply proves this is your number, without a password or a texted code.";

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private string _phoneMasked = string.Empty;

    [ObservableProperty]
    private bool _rememberDevice = true;

    public const string HandoffFailedMessage = "We could not start the WhatsApp handoff. Please try again.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool IsNotBusy => !IsBusy;
    public string MessageText => flow.LoginMessage;
    public string SendHint => $"Goes to Pickup Pal bot, {options.BotDisplayNumber}. Nothing is sent until you tap.";
    public string AccountLine => $"Pickup Pal account · {PhoneMasked}";

    public void Initialize(PhoneSignInStartResponse pendingSignIn)
    {
        DisplayName = pendingSignIn.DisplayName ?? "Your account";
        Initials = ToInitials(DisplayName);
        PhoneMasked = pendingSignIn.PhoneMasked ?? string.Empty;
        RememberDevice = flow.RememberDevice;
        OnPropertyChanged(nameof(AccountLine));
    }

    partial void OnRememberDeviceChanged(bool value) => flow.RememberDevice = value;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ContinueWithWhatsAppAsync(CancellationToken cancellationToken)
    {
        StatusMessage = string.Empty;
        try
        {
            IsBusy = true;
            await flow.StartSignInHandoffAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            StatusMessage = HandoffFailedMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task UseDifferentNumberAsync(CancellationToken cancellationToken)
    {
        flow.Reset();
        return navigator.PopToWelcomeAsync(cancellationToken);
    }

    [RelayCommand]
    private Task GoBackAsync(CancellationToken cancellationToken) => UseDifferentNumberAsync(cancellationToken);

    private static string ToInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? "?"
            : string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }
}
