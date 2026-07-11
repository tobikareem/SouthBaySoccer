using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;

namespace SouthBaySoccer.Services.Authentication;

public sealed class AuthenticationNavigator(IServiceProvider services) : IAuthenticationNavigator
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
            // navigation state. Assign it first, then queue the initial Shell route after the
            // handler has had a turn to attach on Android.
            var shell = services.GetRequiredService<AppShell>();
            window.Page = shell;
            shell.Dispatcher.DispatchDelayed(
                TimeSpan.FromMilliseconds(100),
                () => shell.GoToAsync("//sessions").FireAndForgetSafeAsync());
        });
    }
}
