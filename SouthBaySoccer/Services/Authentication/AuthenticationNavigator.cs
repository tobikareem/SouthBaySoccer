using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using SouthBaySoccer.Pages;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Clients;

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
                NavigateToInitialRouteAsync(shell, cancellationToken).FireAndForgetSafeAsync(errorHandler);
            }

            shell.Loaded += OnShellLoaded;
            window.Page = shell;
        });
    }

    // Routes to the blocking group-link step when the player belongs to no group yet; otherwise lands
    // on the Sessions tab. A failure resolving link status must not trap the user on a blank shell, so
    // any error falls through to //sessions.
    private async Task NavigateToInitialRouteAsync(AppShell shell, CancellationToken cancellationToken)
    {
        var route = "//sessions";
        try
        {
            var groupsClient = services.GetRequiredService<IGroupsClient>();
            var myGroups = await groupsClient.GetMyGroupsAsync(cancellationToken);
            if (!myGroups.IsLinked)
            {
                route = "//link-group";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Non-fatal: enter the app on the aggregate view rather than blocking on a link check.
        }

        await shell.GoToAsync(route);
    }

    public Task ShowSignInAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Same main-thread marshalling as ShowAuthenticatedAppAsync: sign-out is triggered from a
        // UI command, but keep it explicit for parity and future callers.
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var window = Application.Current?.Windows.FirstOrDefault()
                ?? throw new InvalidOperationException("The application window is not available.");

            // WelcomeBackPage is registered transient, so this is a fresh sign-in screen with no
            // carried-over state. Mirrors the initial window in App.CreateWindow.
            var welcomeBackPage = services.GetRequiredService<WelcomeBackPage>();
            window.Page = new NavigationPage(welcomeBackPage)
            {
                BarBackgroundColor = Colors.Transparent,
            };
        });
    }
}
