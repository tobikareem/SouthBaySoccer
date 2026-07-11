using SouthBaySoccer.Pages;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAuthenticationCoordinator _authenticationCoordinator;
    private readonly StartupErrorHandler _callbackErrorHandler;

    public App(
        IServiceProvider serviceProvider,
        IAuthenticationCoordinator authenticationCoordinator,
        StartupErrorHandler callbackErrorHandler)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        _authenticationCoordinator = authenticationCoordinator;
        _callbackErrorHandler = callbackErrorHandler;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var welcomeBackPage = _serviceProvider.GetRequiredService<WelcomeBackPage>();
        var window = new Window(new NavigationPage(welcomeBackPage)
        {
            BarBackgroundColor = Colors.Transparent
        });
        return window;
    }

    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        base.OnAppLinkRequestReceived(uri);
        _authenticationCoordinator.HandleCallbackAsync(uri).FireAndForgetSafeAsync(_callbackErrorHandler);
    }
}
