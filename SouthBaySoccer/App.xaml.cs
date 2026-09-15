using SouthBaySoccer.Pages;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAuthenticationCoordinator _authenticationCoordinator;
    private readonly IOnboardingFlow _onboardingFlow;
    private readonly StartupErrorHandler _callbackErrorHandler;
    private readonly AppLifecycleState _appLifecycleState;

    public App(
        IServiceProvider serviceProvider,
        IAuthenticationCoordinator authenticationCoordinator,
        IOnboardingFlow onboardingFlow,
        StartupErrorHandler callbackErrorHandler,
        AppLifecycleState appLifecycleState)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        _authenticationCoordinator = authenticationCoordinator;
        _onboardingFlow = onboardingFlow;
        _callbackErrorHandler = callbackErrorHandler;
        _appLifecycleState = appLifecycleState;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var welcomeBackPage = _serviceProvider.GetRequiredService<WelcomeBackPage>();
        var window = new Window(new NavigationPage(welcomeBackPage)
        {
            BarBackgroundColor = Colors.Transparent
        });
        window.Activated += (_, _) => _appLifecycleState.SetActive(true);
        window.Resumed += (_, _) => _appLifecycleState.SetActive(true);
        window.Deactivated += (_, _) => _appLifecycleState.SetActive(false);
        window.Stopped += (_, _) => _appLifecycleState.SetActive(false);
#if DEBUG
        // Debug/demo aid: N9JABAY_ONBOARDING_SCREEN=<signup-start|signup-waiting|signup-expired|
        // signup-details|signup-welcome|signin-verify|signin-waiting> opens that onboarding screen on
        // launch (Seed data), so the flow can be reviewed without a WhatsApp round-trip.
        var debugScreenOpened = false;
        welcomeBackPage.Loaded += (_, _) =>
        {
            if (debugScreenOpened)
            {
                return;
            }

            debugScreenOpened = true;
            OpenDebugOnboardingScreenAsync().FireAndForgetSafeAsync(_callbackErrorHandler);
        };
#endif
        return window;
    }

#if DEBUG
    private async Task OpenDebugOnboardingScreenAsync()
    {
        var screen = Environment.GetEnvironmentVariable("N9JABAY_ONBOARDING_SCREEN");
        if (string.IsNullOrWhiteSpace(screen))
        {
            return;
        }

        await Task.Delay(400);
        var navigator = _serviceProvider.GetRequiredService<IOnboardingNavigator>();
        var pendingSignIn = new Contracts.Authentication.PhoneSignInStartResponse(true, "+1 (555) ••• 9421", "Ada Okafor", null);
        switch (screen.Trim().ToLowerInvariant())
        {
            case "signup-start": await navigator.ShowSignUpStartAsync(CancellationToken.None); break;
            case "signup-waiting": await _onboardingFlow.StartSignUpHandoffAsync(CancellationToken.None); break;
            case "signup-expired": await navigator.ShowSignUpExpiredAsync(OnboardingTokenFailure.Expired, CancellationToken.None); break;
            case "signup-details": await _onboardingFlow.HandleRegistrationTokenAsync("demo", CancellationToken.None); break;
            case "signup-welcome":
                await navigator.ShowSignUpWelcomeAsync(
                    new Contracts.Authentication.RegistrationCompletedResponse(
                        new Contracts.Authentication.AuthenticationTokensResponse("seed-access-token", "seed-refresh-token", DateTime.UtcNow.AddHours(1)),
                        "Ada", "+1 (555) ••• 9421", ["Saturday crew"], true),
                    CancellationToken.None);
                break;
            case "signin-verify": await _onboardingFlow.BeginSignInVerificationAsync(pendingSignIn, CancellationToken.None); break;
            case "signin-waiting":
                await _onboardingFlow.BeginSignInVerificationAsync(pendingSignIn, CancellationToken.None);
                await _onboardingFlow.StartSignInHandoffAsync(CancellationToken.None);
                break;
        }
    }
#endif

    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        base.OnAppLinkRequestReceived(uri);
        RouteAppLinkAsync(uri).FireAndForgetSafeAsync(_callbackErrorHandler);
    }

    // Register/login links from the Pickup Pal bot go to the onboarding flow; anything else falls
    // through to the legacy challenge callback.
    private async Task RouteAppLinkAsync(Uri uri)
    {
        if (!await _onboardingFlow.HandleAppLinkAsync(uri, CancellationToken.None))
        {
            await _authenticationCoordinator.HandleCallbackAsync(uri);
        }
    }
}
