using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Services.Navigation;

/// <summary>
/// Leaves the blocking group-link step for the Sessions tab once the player has linked a group.
/// </summary>
public sealed class ShellGroupLinkNavigator : IGroupLinkNavigator
{
    public Task GoToAuthenticatedAppAsync() => Shell.Current.GoToAsync("//sessions");
}
