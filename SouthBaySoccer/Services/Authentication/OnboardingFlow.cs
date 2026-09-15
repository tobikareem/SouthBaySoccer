using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services;

namespace SouthBaySoccer.Services.Authentication;

public sealed class OnboardingFlow(
    IOnboardingClient onboardingClient,
    IAuthenticationCoordinator authenticationCoordinator,
    IOnboardingNavigator navigator,
    IExternalLauncher externalLauncher,
    IUserDialogService dialogService,
    PickupPalOptions options,
    TimeProvider timeProvider) : IOnboardingFlow
{
    public const string ClientArgument = "n9jabay";
    public const string VerificationFailedTitle = "We couldn't verify that link";
    public const string VerificationFailedMessage =
        "The link didn't match the sign-in you started. Enter your number again to get a fresh one.";
    public const string RegistrationFailedMessage =
        "We could not reach the sign-up service. Check your connection and tap the bot's link again.";
    public const string LoginUnavailableMessage =
        "We could not reach the sign-in service. Check your connection and tap the bot's link again.";

    private readonly SemaphoreSlim _linkLock = new(1, 1);
    private (OnboardingLinkKind Kind, string Token)? _lastDispatched;

    public string RegisterMessage => $"!!register {ClientArgument}";
    public string LoginMessage => $"!!login {ClientArgument}";

    public OnboardingLinkKind? PendingKind { get; private set; }
    public DateTimeOffset? LinkRequestedAt { get; private set; }
    public PhoneSignInStartResponse? PendingSignIn { get; private set; }
    public bool RememberDevice { get; set; } = true;
    public bool LastHandoffFailed { get; private set; }

    public Task ShowSignUpAsync(CancellationToken cancellationToken) =>
        navigator.ShowSignUpStartAsync(cancellationToken);

    public async Task StartSignUpHandoffAsync(CancellationToken cancellationToken)
    {
        PendingSignIn = null;
        await HandoffAsync(OnboardingLinkKind.Register, RegisterMessage, cancellationToken);
    }

    public Task BeginSignInVerificationAsync(PhoneSignInStartResponse pendingSignIn, CancellationToken cancellationToken)
    {
        PendingSignIn = pendingSignIn;
        PendingKind = null;
        LinkRequestedAt = null;
        return navigator.ShowSignInVerifyAsync(pendingSignIn, cancellationToken);
    }

    public Task StartSignInHandoffAsync(CancellationToken cancellationToken) =>
        HandoffAsync(OnboardingLinkKind.Login, LoginMessage, cancellationToken);

    public Task<bool> ReopenWhatsAppAsync(CancellationToken cancellationToken) =>
        PendingKind switch
        {
            OnboardingLinkKind.Register => OpenWhatsAppAsync(RegisterMessage, cancellationToken),
            OnboardingLinkKind.Login => OpenWhatsAppAsync(LoginMessage, cancellationToken),
            _ => Task.FromResult(false)
        };

    public Task<bool> HandleAppLinkAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!TryParseLink(uri, out var kind, out var token))
        {
            return Task.FromResult(false);
        }

        return DispatchAsync(kind, token, cancellationToken);
    }

    public async Task<bool> HandlePastedLinkAsync(string? text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // The bot's reply is a sentence with the link inside it. Try every token that parses as one
        // of our link authorities; plain words like "here:" also parse as URIs, so do not stop early.
        foreach (var part in text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Uri.TryCreate(part.Trim(), UriKind.Absolute, out var uri) &&
                await HandleAppLinkAsync(uri, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    public async Task HandleRegistrationTokenAsync(string token, CancellationToken cancellationToken)
    {
        // Only a definitive token outcome (valid or a known failure) changes flow state. Transport
        // and unexpected errors propagate so the dispatcher can keep the link retryable.
        try
        {
            var validation = await onboardingClient.ValidateRegistrationAsync(token, cancellationToken);
            PendingKind = null;
            await navigator.ShowSignUpDetailsAsync(token, validation.PhoneMasked, cancellationToken);
        }
        catch (OnboardingTokenException ex)
        {
            PendingKind = null;
            await navigator.ShowSignUpExpiredAsync(ex.Failure, cancellationToken);
        }
    }

    public async Task HandleLoginTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (PendingSignIn is null)
        {
            // A login link with no sign-in in progress on this device: nothing to bind it to.
            await dialogService.ShowAlertAsync(VerificationFailedTitle, VerificationFailedMessage, "OK", cancellationToken);
            return;
        }

        try
        {
            var tokens = await onboardingClient.CompleteLoginAsync(token, RememberDevice, cancellationToken);
            Reset();
            await authenticationCoordinator.CompleteSignInAsync(tokens, cancellationToken);
        }
        catch (OnboardingTokenException)
        {
            // Definitive rejection: the pending sign-in is void, start over from the phone number.
            Reset();
            await dialogService.ShowAlertAsync(VerificationFailedTitle, VerificationFailedMessage, "OK", cancellationToken);
            await navigator.PopToWelcomeAsync(cancellationToken);
        }
    }

    public void Reset()
    {
        PendingKind = null;
        LinkRequestedAt = null;
        PendingSignIn = null;
        LastHandoffFailed = false;
        // _lastDispatched is intentionally kept: a redeemed token stays redeemed across flow resets.
    }

    private async Task HandoffAsync(OnboardingLinkKind kind, string message, CancellationToken cancellationToken)
    {
        PendingKind = kind;
        LinkRequestedAt = timeProvider.GetUtcNow();
        LastHandoffFailed = !await OpenWhatsAppAsync(message, cancellationToken);
        await navigator.ShowLinkWaitingAsync(kind, cancellationToken);
    }

    private async Task<bool> OpenWhatsAppAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            return await externalLauncher.OpenWhatsAppMessageAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<bool> DispatchAsync(OnboardingLinkKind kind, string token, CancellationToken cancellationToken)
    {
        // Links can arrive twice (cold start + warm intent); serialize and drop the duplicate so a
        // token is redeemed once and the page is pushed once.
        await _linkLock.WaitAsync(cancellationToken);
        try
        {
            // Checked inside the lock so a duplicate queued behind a successful login sees the
            // signed-in state instead of re-running with no pending sign-in.
            if (authenticationCoordinator.IsAuthenticated || _lastDispatched == (kind, token))
            {
                return true;
            }

            _lastDispatched = (kind, token);
            try
            {
                if (kind == OnboardingLinkKind.Register)
                {
                    await HandleRegistrationTokenAsync(token, cancellationToken);
                }
                else
                {
                    await HandleLoginTokenAsync(token, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _lastDispatched = null;
                throw;
            }
            catch (Exception)
            {
                // Transport, timeout, or unexpected failure: the token was not consumed, so the
                // same link must work again. Keep the flow state and tell the user to retry.
                _lastDispatched = null;
                await dialogService.ShowAlertAsync(
                    kind == OnboardingLinkKind.Register ? "Sign-up unavailable" : "Sign-in unavailable",
                    kind == OnboardingLinkKind.Register ? RegistrationFailedMessage : LoginUnavailableMessage,
                    "OK",
                    cancellationToken);
            }

            return true;
        }
        finally
        {
            _linkLock.Release();
        }
    }

    private bool TryParseLink(Uri uri, out OnboardingLinkKind kind, out string token)
    {
        kind = default;
        token = string.Empty;

        var isCallbackHost = SameAuthority(uri, options.CallbackUri);
        var isAppLinkHost = SameAuthority(uri, options.AppLinkBaseUri);
        if (!isCallbackHost && !isAppLinkHost)
        {
            return false;
        }

        var lastSegment = uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
        OnboardingLinkKind? parsedKind = lastSegment.ToLowerInvariant() switch
        {
            "register" => OnboardingLinkKind.Register,
            "login" => OnboardingLinkKind.Login,
            _ => null
        };
        if (parsedKind is not { } resolvedKind)
        {
            return false;
        }

        kind = resolvedKind;

        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (string.Equals(pair[..separator], "token", StringComparison.OrdinalIgnoreCase))
            {
                token = Uri.UnescapeDataString(pair[(separator + 1)..]);
                return !string.IsNullOrWhiteSpace(token);
            }
        }

        return false;
    }

    private static bool SameAuthority(Uri uri, Uri expected) =>
        string.Equals(uri.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.Host, expected.Host, StringComparison.OrdinalIgnoreCase);
}
