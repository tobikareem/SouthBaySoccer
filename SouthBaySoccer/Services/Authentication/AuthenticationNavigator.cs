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

        window.Page = services.GetRequiredService<AppShell>();
        return Task.CompletedTask;
    }
}
