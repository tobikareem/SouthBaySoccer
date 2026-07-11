namespace SouthBaySoccer.Services;

/// <summary>
/// Error handler safe to use before the authenticated Shell exists. Startup session restore and
/// the WhatsApp auth-callback path can both run while the window is still showing
/// <c>WelcomeBackPage</c> inside a <c>NavigationPage</c> — before any Shell is current, which
/// makes <see cref="ModalErrorHandler"/> a silent no-op. Delegates to
/// <see cref="ModalErrorHandler"/> once a Shell is current; otherwise resolves the window's
/// current page and displays the alert directly.
/// </summary>
public sealed class StartupErrorHandler(ModalErrorHandler modalErrorHandler) : IErrorHandler
{
    /// <summary>
    /// Handle error in UI.
    /// </summary>
    /// <param name="ex">Exception being thrown.</param>
    public void HandleError(Exception ex)
    {
        if (Shell.Current is Shell)
        {
            modalErrorHandler.HandleError(ex);
            return;
        }

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        page?.Dispatcher.Dispatch(() =>
            page.DisplayAlertAsync("Error", ex.Message, "OK").FireAndForgetSafeAsync());
    }
}
