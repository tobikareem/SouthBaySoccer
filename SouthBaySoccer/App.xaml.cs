using SouthBaySoccer.Pages;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAuthenticationCoordinator _authenticationCoordinator;
    private readonly StartupErrorHandler _callbackErrorHandler;
    private readonly AppLifecycleState _appLifecycleState;

    public App(
        IServiceProvider serviceProvider,
        IAuthenticationCoordinator authenticationCoordinator,
        StartupErrorHandler callbackErrorHandler,
        AppLifecycleState appLifecycleState)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        _authenticationCoordinator = authenticationCoordinator;
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
        return window;
    }

    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        base.OnAppLinkRequestReceived(uri);
        _authenticationCoordinator.HandleCallbackAsync(uri).FireAndForgetSafeAsync(_callbackErrorHandler);
    }
}
