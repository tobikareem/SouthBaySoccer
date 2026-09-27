using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.PageModels;

/// <summary>Step 1 of 3: explain the WhatsApp handshake and hand off with !!register prefilled.</summary>
public partial class SignUpStartPageModel(
    IOnboardingFlow flow,
    IOnboardingNavigator navigator,
    PickupPalOptions options) : ObservableObject
{
    public const string StepLabel = "Step 1 of 3";
    public const string Title = "Create your account";
    public const string Heading = "Confirm your number on WhatsApp.";
    public const string Description =
        "WhatsApp only lets the Pickup Pal bot reply to you, never message first. So you send one message, and it sends back a link that opens this app.";
    public const string Step1Title = "Tap the green button";
    public const string Step1Detail = "WhatsApp opens with the message ready.";
    public const string Step2Title = "Send it to the bot";
    public const string Step2Detail = "It replies within seconds with an N9ja Bay link.";
    public const string Step3Title = "Tap the link";
    public const string Step3Detail = "You land back here with your number confirmed.";
    public const string WhyHeading = "Why WhatsApp?";
    public const string WhyMessage =
        "Your group already runs on it. Sending the message proves the number is yours, so there is no code to type and no password to remember.";

    public const string HandoffFailedMessage = "We could not start the WhatsApp handoff. Please try again.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool IsNotBusy => !IsBusy;
    public string MessageText => flow.RegisterMessage;
    public string BotDisplayNumber => options.BotDisplayNumber;
    public string SendHint => $"Goes to Pickup Pal bot, {options.BotDisplayNumber}. Nothing is sent until you tap.";

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ContinueWithWhatsAppAsync(CancellationToken cancellationToken)
    {
        StatusMessage = string.Empty;
        try
        {
            IsBusy = true;
            await flow.StartSignUpHandoffAsync(cancellationToken);
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
    private Task GoBackAsync(CancellationToken cancellationToken) => navigator.PopAsync(cancellationToken);

    [RelayCommand]
    private Task SignInAsync(CancellationToken cancellationToken) => navigator.PopToWelcomeAsync(cancellationToken);
}
