using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Pages;

public partial class WelcomeBackPage : ContentPage
{
    private readonly IAppStartupService _startupService;
    private readonly IErrorHandler _startupErrorHandler;
    private bool _restoreStarted;

    public WelcomeBackPage(
        WelcomeBackPageModel pageModel,
        IAppStartupService startupService,
        ModalErrorHandler modalErrorHandler)
    {
        InitializeComponent();
        BindingContext = pageModel;
        _startupService = startupService;
        _startupErrorHandler = new StartupErrorHandler(this, modalErrorHandler);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (_restoreStarted)
        {
            return;
        }

        _restoreStarted = true;
        Dispatcher.DispatchDelayed(
            TimeSpan.FromSeconds(5),
            () => _startupService.TryRestoreSessionAsync().FireAndForgetSafeAsync(_startupErrorHandler));
    }

    private sealed class StartupErrorHandler(Page page, ModalErrorHandler modalErrorHandler) : IErrorHandler
    {
        public void HandleError(Exception ex)
        {
            if (Shell.Current is Shell)
            {
                modalErrorHandler.HandleError(ex);
                return;
            }

            page.Dispatcher.Dispatch(() =>
                page.DisplayAlertAsync("Error", ex.Message, "OK").FireAndForgetSafeAsync());
        }
    }
}
