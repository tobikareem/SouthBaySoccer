using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Pages;

public partial class WelcomeBackPage : ContentPage
{
    // Short enough that returning users are not kept waiting, long enough to let Android draw the
    // first frame before restore starts (.ai/lessons/2026-07-06-android-defer-session-restore.md).
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(150);

    private readonly IAppStartupService _startupService;
    private readonly StartupErrorHandler _startupErrorHandler;

    public WelcomeBackPage(
        WelcomeBackPageModel pageModel,
        IAppStartupService startupService,
        StartupErrorHandler startupErrorHandler)
    {
        InitializeComponent();
        BindingContext = pageModel;
        _startupService = startupService;
        _startupErrorHandler = startupErrorHandler;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // Unsubscribing (rather than a bool flag) guarantees restore is scheduled exactly once
        // even if Loaded fires again later.
        Loaded -= OnLoaded;
        Dispatcher.DispatchDelayed(
            RestoreDelay,
            () => _startupService.TryRestoreSessionAsync().FireAndForgetSafeAsync(_startupErrorHandler));
    }
}
