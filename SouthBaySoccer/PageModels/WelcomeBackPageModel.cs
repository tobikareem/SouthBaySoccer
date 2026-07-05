using System.Net;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.PageModels;

public partial class WelcomeBackPageModel(
    IAuthenticationClient authenticationClient,
    IAuthenticationCoordinator authenticationCoordinator,
    IExternalLauncher externalLauncher,
    PickupPalOptions options) : ObservableObject
{
    public const string WelcomeLabel = "WELCOME BACK";
    public const string Heading = "Your next game starts here.";
    public const string Description =
        "Sign in with the phone number connected to your Pickup Pal account.";
    public const string SecurityHeading = "Password-free and secure";
    public const string SecurityMessage =
        "Pickup Pal verifies your account. SouthBaySoccer stores only app session tokens on this device.";
    public const string BotHelpMessage = "Need help? Open the Pickup Pal bot for account support.";
    public const string SignupHelpMessage =
        "Create your account on the web, then come back and sign in with your phone number.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoneNumberError))]
    private string _phoneNumberError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _phoneNumber = string.Empty;

    public string BotDisplayNumber => options.BotDisplayNumber;
    public bool HasPhoneNumberError => !string.IsNullOrWhiteSpace(PhoneNumberError);
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool IsNotBusy => !IsBusy;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RequestWhatsAppChallengeAsync(CancellationToken cancellationToken)
    {
        PhoneNumberError = string.Empty;
        StatusMessage = string.Empty;

        if (!PhoneNumberValidator.TryNormalize(PhoneNumber, out var normalizedPhoneNumber))
        {
            PhoneNumberError = "Enter a valid international phone number, including country code.";
            return;
        }

        try
        {
            IsBusy = true;
            var tokens = await authenticationClient.SignInByPhoneAsync(
                normalizedPhoneNumber,
                cancellationToken);

            await authenticationCoordinator.CompleteSignInAsync(tokens, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            StatusMessage = "We couldn't find that phone number on Pickup Pal. Sign up on Pickup Pal, then come back.";
        }
        catch (HttpRequestException)
        {
            StatusMessage =
                "We could not reach the sign-in service. Check your connection and try again.";
        }
        catch (Exception)
        {
            StatusMessage = "We could not start sign-in. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task OpenPickupPalBotAsync(CancellationToken cancellationToken) =>
        OpenExternalAsync(
            () => externalLauncher.OpenPickupPalBotAsync(cancellationToken),
            "WhatsApp could not be opened on this device.");

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task OpenPickupPalSignupAsync(CancellationToken cancellationToken) =>
        OpenExternalAsync(
            () => externalLauncher.OpenPickupPalSignupAsync(cancellationToken),
            "The Pickup Pal signup page could not be opened.");

    private async Task OpenExternalAsync(Func<Task<bool>> open, string failureMessage)
    {
        StatusMessage = string.Empty;
        try
        {
            if (!await open())
            {
                StatusMessage = failureMessage;
            }
        }
        catch (Exception)
        {
            StatusMessage = failureMessage;
        }
    }
}
