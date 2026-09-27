namespace SouthBaySoccer.Services.Authentication;

public interface IAuthenticationNavigator
{
    Task ShowAuthenticatedAppAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows the authenticated app on the "join your groups" step even when the player already
    /// belongs to a group, so a newly registered player can choose more groups than the ones
    /// Pickup Pal auto-approved from their WhatsApp membership.
    /// </summary>
    Task ShowGroupChoiceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Swaps the window back to the sign-in (Welcome Back) screen. Used by sign-out so the session
    /// ends immediately and a different account can sign in without relaunching the app.
    /// </summary>
    Task ShowSignInAsync(CancellationToken cancellationToken = default);
}
