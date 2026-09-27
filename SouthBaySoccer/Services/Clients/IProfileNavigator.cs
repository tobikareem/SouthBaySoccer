namespace SouthBaySoccer.Services.Clients;

public interface IProfileNavigator
{
    Task OpenLeaderboardAsync();

    /// <summary>
    /// Pops the pushed profile detail page. Used when viewing another player's profile, which is a
    /// detail route rather than the Profile tab.
    /// </summary>
    Task GoBackAsync();

    /// <summary>Pushes the "My groups" membership screen (route <c>my-groups</c>).</summary>
    Task OpenMyGroupsAsync();

    /// <summary>Pushes the members screen for one group the player administers (route <c>group-members</c>).</summary>
    Task OpenGroupMembersAsync(Guid groupChatId);

    /// <summary>Pushes the super-admin "Groups &amp; admins" list (route <c>super-admin-groups</c>).</summary>
    Task OpenSuperAdminGroupsAsync();
}
