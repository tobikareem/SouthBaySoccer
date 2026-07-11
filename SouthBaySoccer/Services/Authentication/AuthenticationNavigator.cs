using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using SouthBaySoccer.Services;

namespace SouthBaySoccer.Services.Authentication;

public sealed class AuthenticationNavigator(
    IServiceProvider services,
    StartupErrorHandler errorHandler) : IAuthenticationNavigator
{
    public Task ShowAuthenticatedAppAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Window.Page is a UI element and must be mutated on the main thread. Callers can arrive on
        // background continuations (startup token refresh, deep-link callback), so marshal explicitly.
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var window = Application.Current?.Windows.FirstOrDefault()
                ?? throw new InvalidOperationException("The application window is not available.");

            // AppShell is registered transient, so this is always a fresh shell with no prior
            // navigation state. Subscribe before assigning the window's page so we cannot miss
            // Loaded firing as part of that assignment, then navigate exactly once the Shell's
            // handler has actually attached instead of guessing with a fixed delay.
            var shell = services.GetRequiredService<AppShell>();

            void OnShellLoaded(object? sender, EventArgs e)
            {
                shell.Loaded -= OnShellLoaded;
                shell.GoToAsync("//sessions").FireAndForgetSafeAsync(errorHandler);
            }

            shell.Loaded += OnShellLoaded;
            window.Page = shell;
        });
    }
}
