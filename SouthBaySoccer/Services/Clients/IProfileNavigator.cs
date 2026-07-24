namespace SouthBaySoccer.Services.Clients;

public interface IProfileNavigator
{
    Task OpenLeaderboardAsync();

    /// <summary>
    /// Pops the pushed profile detail page. Used when viewing another player's profile, which is a
    /// detail route rather than the Profile tab.
    /// </summary>
    Task GoBackAsync();
}
