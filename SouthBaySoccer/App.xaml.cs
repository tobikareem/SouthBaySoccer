using SouthBaySoccer.Pages;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAuthenticationCoordinator _authenticationCoordinator;
    private readonly IAppStartupService _startupService;

    public App(
        IServiceProvider serviceProvider,
        IAuthenticationCoordinator authenticationCoordinator,
        IAppStartupService startupService)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        _authenticationCoordinator = authenticationCoordinator;
        _startupService = startupService;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var welcomeBackPage = _serviceProvider.GetRequiredService<WelcomeBackPage>();
        var window = new Window(new NavigationPage(welcomeBackPage)
        {
            BarBackgroundColor = Colors.Transparent
        });
        window.Created += (_, _) => _startupService.TryRestoreSessionAsync().FireAndForgetSafeAsync();
        return window;
    }

    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        base.OnAppLinkRequestReceived(uri);
        _authenticationCoordinator.HandleCallbackAsync(uri).FireAndForgetSafeAsync();
    }
}
