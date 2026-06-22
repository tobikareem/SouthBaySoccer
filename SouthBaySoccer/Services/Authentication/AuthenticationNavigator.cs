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
            // navigation state, and Sessions is the first ShellContent in the TabBar — assigning it
            // as the window page lands on the Sessions tab (//sessions) by default. Do NOT call
            // GoToAsync here: invoking Shell navigation before the shell handler exists crashes.
            window.Page = services.GetRequiredService<AppShell>();
        });
    }
}
