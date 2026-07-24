namespace SouthBaySoccer.Services.Authentication;

public interface IAuthenticationNavigator
{
    Task ShowAuthenticatedAppAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Swaps the window back to the sign-in (Welcome Back) screen. Used by sign-out so the session
    /// ends immediately and a different account can sign in without relaunching the app.
    /// </summary>
    Task ShowSignInAsync(CancellationToken cancellationToken = default);
}
