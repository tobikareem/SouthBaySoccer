using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.PageModels;

/// <summary>Step 2b: the bot link expired, was already used, or belongs to an existing account.</summary>
public partial class SignUpExpiredPageModel(
    IOnboardingFlow flow,
    IOnboardingNavigator navigator) : ObservableObject
{
    public const string StepLabel = "Step 2 of 3";
    public const string ExpiredTitle = "Link expired";
    public const string ExpiredHeading = "That link has expired.";
    public const string ExpiredMessage =
        "Links from the bot only last 15 minutes and work once. Nothing was created, so just ask for a fresh one.";
    public const string InvalidHeading = "That link has already been used.";
    public const string InvalidMessage =
        "Each link from the bot works once. Nothing was created, so just ask for a fresh one.";
    public const string RegisteredHeading = "You already have an account.";
    public const string RegisteredMessage =
        "The bot recognised your number. Go back and sign in with your phone instead.";
    public const string ExistingAccountHint =
        "If the bot says you already have an account, you're set. Go back and sign in with your phone.";

    [ObservableProperty]
    private string _heading = ExpiredHeading;

    [ObservableProperty]
    private string _message = ExpiredMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotAlreadyRegistered))]
    private bool _isAlreadyRegistered;

    public bool IsNotAlreadyRegistered => !IsAlreadyRegistered;

    public void Initialize(OnboardingTokenFailure failure)
    {
        (Heading, Message, IsAlreadyRegistered) = failure switch
        {
            OnboardingTokenFailure.AlreadyRegistered => (RegisteredHeading, RegisteredMessage, true),
            OnboardingTokenFailure.Invalid or OnboardingTokenFailure.Mismatch => (InvalidHeading, InvalidMessage, false),
            _ => (ExpiredHeading, ExpiredMessage, false)
        };
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task SendAgainAsync(CancellationToken cancellationToken)
    {
        flow.Reset();
        return flow.StartSignUpHandoffAsync(cancellationToken);
    }

    [RelayCommand]
    private Task SignInAsync(CancellationToken cancellationToken)
    {
        flow.Reset();
        return navigator.PopToWelcomeAsync(cancellationToken);
    }

    [RelayCommand]
    private Task GoBackAsync(CancellationToken cancellationToken) => navigator.PopToWelcomeAsync(cancellationToken);
}
