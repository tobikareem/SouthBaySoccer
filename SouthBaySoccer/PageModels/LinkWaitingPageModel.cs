using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.PageModels;

/// <summary>
/// Shared "Check your WhatsApp" screen for sign-up (Step 2 of 3) and sign-in verification. Runs a
/// local countdown against the bot link's 15-minute lifetime; the server remains authoritative.
/// </summary>
public partial class LinkWaitingPageModel(
    IOnboardingFlow flow,
    IOnboardingNavigator navigator,
    IClipboardReader clipboardReader,
    IPollingDelay pollingDelay,
    TimeProvider timeProvider) : ObservableObject
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(15);
    public const string Title = "Waiting for WhatsApp";
    public const string Heading = "Check your WhatsApp.";
    public const string RegisterDescription =
        "Once you've sent the message, the Pickup Pal bot replies with a link. Tap it and you'll come straight back here.";
    public const string LoginDescription =
        "Tap the link the Pickup Pal bot sent you. You'll land on your sessions, already signed in.";
    public const string RegisterExpiryNote = "The clock started when the bot answered, not when you opened this screen.";
    public const string LoginExpiryNote = "Links work once. If it expires, send the message again from the previous screen.";
    public const string WhatsAppUnavailableMessage =
        "WhatsApp could not be opened on this device. Install it, or paste the bot's link below.";
    public const string PasteFailedMessage = "That doesn't look like a link from the Pickup Pal bot.";
    public const string PasteLinkPlaceholder = "Paste the bot's link here";
    public const string RegisterExpiredHint = "This link has expired. Ask the bot for a fresh one to keep going.";
    public const string LoginExpiredHint = "This link has expired. Go back and enter your number again for a fresh one.";

    private CancellationTokenSource? _countdown;

    /// <summary>
    /// The visible text field the player can type or OS-paste a link into. Submitting with this
    /// empty falls back to reading the OS clipboard directly, preserving the original one-tap
    /// convenience for anyone who already has the link copied.
    /// </summary>
    [ObservableProperty]
    private string _pastedLinkText = string.Empty;

    [ObservableProperty]
    private OnboardingLinkKind _kind;

    [ObservableProperty]
    private string _stepLabel = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _expiryNote = string.Empty;

    [ObservableProperty]
    private string _noReplyHint = string.Empty;

    [ObservableProperty]
    private string _remainingText = "15:00";

    [ObservableProperty]
    private int _remainingPercent = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotExpired))]
    private bool _hasExpired;

    public bool HasNotExpired => !HasExpired;
    public string ExpiredHint => Kind == OnboardingLinkKind.Register ? RegisterExpiredHint : LoginExpiredHint;
    public string ExpiredActionText => Kind == OnboardingLinkKind.Register ? "Link expired? Get a new one" : "Link expired? Start over";

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool IsRegister => Kind == OnboardingLinkKind.Register;

    public void Initialize(OnboardingLinkKind kind)
    {
        Kind = kind;
        var message = kind == OnboardingLinkKind.Register ? flow.RegisterMessage : flow.LoginMessage;
        StepLabel = kind == OnboardingLinkKind.Register ? "Step 2 of 3" : "Sign in";
        Description = kind == OnboardingLinkKind.Register ? RegisterDescription : LoginDescription;
        ExpiryNote = kind == OnboardingLinkKind.Register ? RegisterExpiryNote : LoginExpiryNote;
        NoReplyHint = kind == OnboardingLinkKind.Register
            ? $"Didn't get a reply? Make sure you sent {message} to the bot, not to your group."
            : $"No reply? Make sure {message} went to the bot from the number you signed in with, not from another phone.";
        StatusMessage = flow.LastHandoffFailed ? WhatsAppUnavailableMessage : string.Empty;
        OnPropertyChanged(nameof(IsRegister));
        OnPropertyChanged(nameof(ExpiredHint));
        OnPropertyChanged(nameof(ExpiredActionText));
    }

    /// <summary>Advances the countdown by one tick. Public so tests can drive it without a timer.</summary>
    public void Tick()
    {
        var requestedAt = flow.LinkRequestedAt ?? timeProvider.GetUtcNow();
        var remaining = LinkLifetime - (timeProvider.GetUtcNow() - requestedAt);
        if (remaining <= TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
            HasExpired = true;
        }

        RemainingText = $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}";
        RemainingPercent = (int)Math.Clamp(Math.Round(remaining.TotalSeconds / LinkLifetime.TotalSeconds * 100), 0, 100);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task AppearingAsync()
    {
        StopCountdown();
        var cts = new CancellationTokenSource();
        _countdown = cts;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                Tick();
                if (HasExpired)
                {
                    break;
                }

                await pollingDelay.DelayAsync(TimeSpan.FromSeconds(1), cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Screen went away; countdown stops silently.
        }
    }

    [RelayCommand]
    private void Disappearing() => StopCountdown();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenWhatsAppAgainAsync(CancellationToken cancellationToken)
    {
        try
        {
            StatusMessage = await flow.ReopenWhatsAppAsync(cancellationToken) ? string.Empty : WhatsAppUnavailableMessage;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            StatusMessage = WhatsAppUnavailableMessage;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task PasteLinkAsync(CancellationToken cancellationToken)
    {
        StatusMessage = string.Empty;
        try
        {
            // Prefer whatever the player typed or OS-pasted into the visible field; only fall
            // back to reading the clipboard directly when they left it empty and tapped the
            // button as a one-tap shortcut.
            var text = string.IsNullOrWhiteSpace(PastedLinkText)
                ? await clipboardReader.GetTextAsync(cancellationToken)
                : PastedLinkText;
            if (!await flow.HandlePastedLinkAsync(text, cancellationToken))
            {
                StatusMessage = PasteFailedMessage;
            }
            else
            {
                PastedLinkText = string.Empty;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            StatusMessage = PasteFailedMessage;
        }
    }

    /// <summary>Register: go to the expired screen for a fresh handoff. Login: back to the phone number.</summary>
    [RelayCommand]
    private Task LinkExpiredAsync(CancellationToken cancellationToken)
    {
        if (Kind == OnboardingLinkKind.Register)
        {
            return navigator.ShowSignUpExpiredAsync(OnboardingTokenFailure.Expired, cancellationToken);
        }

        flow.Reset();
        return navigator.PopToWelcomeAsync(cancellationToken);
    }

    [RelayCommand]
    private Task GoBackAsync(CancellationToken cancellationToken) => navigator.PopAsync(cancellationToken);

    private void StopCountdown()
    {
        _countdown?.Cancel();
        _countdown?.Dispose();
        _countdown = null;
    }
}
