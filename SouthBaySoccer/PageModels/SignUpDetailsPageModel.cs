using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.PageModels;

/// <summary>Step 3 of 3: the details form. The phone is already proven by the registration token.</summary>
public partial class SignUpDetailsPageModel(
    IOnboardingClient onboardingClient,
    IOnboardingNavigator navigator,
    IExternalLauncher externalLauncher,
    TimeProvider timeProvider) : ObservableObject
{
    public const string StepLabel = "Step 3 of 3";
    public const string Title = "About you";
    public const string ConfirmedHeading = "Number confirmed";
    public const string PasswordHint = "At least 6 characters. Used only on the Pickup Pal website.";
    public const string NextStepHint = "Your waiver and code of conduct come next, before your first RSVP.";
    public const string EmailTakenMessage = "An account already uses this email.";
    public const string ServiceUnavailableMessage =
        "We could not reach the sign-up service. Check your connection and try again.";
    public const string TermsTitle = "I agree to the terms";
    public const string TermsSubtitle = "Read them below before you agree.";
    public const string LinkOpenFailedMessage = "That page could not be opened on this device.";
    public const int MinimumPasswordLength = 6;

    private static readonly Regex EmailPattern = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);
    private string _token = string.Empty;

    public ObservableCollection<PositionOption> Positions { get; } =
    [
        new("ST", "Striker"), new("RW", "Right wing"), new("LW", "Left wing"),
        new("CM", "Midfield"), new("CB", "Centre back"), new("GK", "Goalkeeper")
    ];

    [ObservableProperty]
    private string _phoneMasked = string.Empty;

    [ObservableProperty]
    private string _firstName = string.Empty;

    [ObservableProperty]
    private string _lastName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmailError))]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private PositionOption? _selectedPosition;

    [ObservableProperty]
    private bool _termsAccepted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmailError))]
    [NotifyPropertyChangedFor(nameof(IsEmailTaken))]
    private string _emailError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormError))]
    private string _formError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public string ConfirmedMessage => $"{PhoneMasked} is linked to your WhatsApp. This is how you'll sign in.";
    public bool HasEmailError => !string.IsNullOrWhiteSpace(EmailError);
    public bool IsEmailTaken => EmailError == EmailTakenMessage;
    public bool HasFormError => !string.IsNullOrWhiteSpace(FormError);
    public bool IsNotBusy => !IsBusy;

    public void Initialize(string token, string phoneMasked)
    {
        _token = token;
        PhoneMasked = phoneMasked;
        OnPropertyChanged(nameof(ConfirmedMessage));
    }

    partial void OnEmailChanged(string value) => EmailError = string.Empty;

    /// <summary>Tapping the selected chip again clears the optional position.</summary>
    [RelayCommand]
    private void TogglePosition(PositionOption? option) =>
        SelectedPosition = option is null || ReferenceEquals(option, SelectedPosition) ? null : option;

    partial void OnSelectedPositionChanged(PositionOption? oldValue, PositionOption? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task OpenTermsAsync(CancellationToken cancellationToken) =>
        OpenLinkAsync(() => externalLauncher.OpenTermsAsync(cancellationToken));

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task OpenPrivacyPolicyAsync(CancellationToken cancellationToken) =>
        OpenLinkAsync(() => externalLauncher.OpenPrivacyPolicyAsync(cancellationToken));

    private async Task OpenLinkAsync(Func<Task<bool>> open)
    {
        try
        {
            FormError = await open() ? string.Empty : LinkOpenFailedMessage;
        }
        catch (Exception)
        {
            FormError = LinkOpenFailedMessage;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task CreateAccountAsync(CancellationToken cancellationToken)
    {
        FormError = string.Empty;
        EmailError = string.Empty;
        if (!Validate())
        {
            return;
        }

        try
        {
            IsBusy = true;
            var email = Email.Trim();
            if (!await onboardingClient.IsEmailAvailableAsync(email, cancellationToken))
            {
                EmailError = EmailTakenMessage;
                return;
            }

            var termsVersion = await onboardingClient.GetTermsVersionAsync(cancellationToken);
            var registration = await onboardingClient.RegisterAsync(
                new RegisterWithWhatsAppRequest(
                    _token,
                    FirstName.Trim(),
                    LastName.Trim(),
                    email,
                    Password,
                    SelectedPosition?.Code,
                    termsVersion,
                    timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken);

            await navigator.ShowSignUpWelcomeAsync(registration, cancellationToken);
        }
        catch (OnboardingTokenException ex)
        {
            await navigator.ShowSignUpExpiredAsync(ex.Failure, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Timeouts (TaskCanceledException), empty bodies, and transport errors all land here.
            FormError = ServiceUnavailableMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task SignInInsteadAsync(CancellationToken cancellationToken) => navigator.PopToWelcomeAsync(cancellationToken);

    [RelayCommand]
    private Task GoBackAsync(CancellationToken cancellationToken) => navigator.PopAsync(cancellationToken);

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
        {
            FormError = "Enter your first and last name.";
            return false;
        }

        if (!EmailPattern.IsMatch(Email.Trim()))
        {
            EmailError = "Enter a valid email address.";
            return false;
        }

        if (Password.Length < MinimumPasswordLength)
        {
            FormError = $"Your password needs at least {MinimumPasswordLength} characters.";
            return false;
        }

        if (!TermsAccepted)
        {
            FormError = "Accept the terms and privacy policy to continue.";
            return false;
        }

        return true;
    }
}

/// <summary>A selectable position chip; <see cref="IsSelected"/> drives the shared SelectableChip style.</summary>
public sealed partial class PositionOption(string code, string name) : ObservableObject
{
    public string Code { get; } = code;
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected;
}
