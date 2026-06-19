using Microsoft.Extensions.DependencyInjection;

namespace SouthBaySoccer.Services.Authentication;

public sealed class AuthenticationNavigator(IServiceProvider services) : IAuthenticationNavigator
{
    public Task ShowAuthenticatedAppAsync()
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window is null)
        {
            throw new InvalidOperationException("The application window is not available.");
        }

        // AppShell is registered transient, so this is always a fresh shell with no prior navigation
        // state, and Sessions is the first ShellContent in the TabBar — assigning it as the window
        // page lands on the Sessions tab (//sessions) by default. Do NOT call GoToAsync here: invoking
        // Shell navigation before the shell's handler is created crashes on assignment.
        window.Page = services.GetRequiredService<AppShell>();
        return Task.CompletedTask;
    }
}
