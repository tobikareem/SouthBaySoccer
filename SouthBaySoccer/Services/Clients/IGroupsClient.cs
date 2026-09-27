using SouthBaySoccer.Contracts.Groups;

namespace SouthBaySoccer.Services.Clients;

/// <summary>
/// Client for WhatsApp group membership. Reads and writes all go to our own backend, which owns the
/// player-to-group linkage (it only reads from Pickup Pal, never writes to it). The server decides
/// every membership outcome — approve-on-request when Pickup Pal already lists the player in that
/// WhatsApp group, otherwise Pending until a group admin approves — and the client only shows it.
/// </summary>
public interface IGroupsClient
{
    /// <summary>Gets every group a player can choose from at the sign-in link step (legacy shape, mapped from the catalogue).</summary>
    Task<IReadOnlyList<GroupChatDto>> GetAvailableGroupsAsync(CancellationToken cancellationToken);

    /// <summary>Gets the current player's linked groups and whether the link step is satisfied (at least one Approved membership).</summary>
    Task<MyGroupsResponse> GetMyGroupsAsync(CancellationToken cancellationToken);

    /// <summary>Links the current player to the group with the given external id (legacy single-group link).</summary>
    Task<MyGroupsResponse> LinkAsync(string groupExternalId, CancellationToken cancellationToken);

    /// <summary>Gets every group with the caller's own membership state on each.</summary>
    Task<IReadOnlyList<GroupWithMembershipDto>> GetCatalogAsync(CancellationToken cancellationToken);

    /// <summary>Gets the caller's memberships plus their global standing (super admin, has an approved group).</summary>
    Task<MyGroupMembershipsResponse> GetMyMembershipsAsync(CancellationToken cancellationToken);

    /// <summary>Requests membership of every listed group; the server approves or leaves each Pending.</summary>
    Task<MyGroupMembershipsResponse> RequestMembershipsAsync(IReadOnlyList<Guid> groupChatIds, CancellationToken cancellationToken);

    /// <summary>Leaves (or withdraws a pending request for) one group.</summary>
    Task LeaveAsync(Guid groupChatId, CancellationToken cancellationToken);

    /// <summary>Gets the pending requests and current members of a group the caller administers.</summary>
    Task<GroupMembersResponse> GetMembersAsync(Guid groupChatId, CancellationToken cancellationToken);

    /// <summary>Approves a pending request.</summary>
    Task ApproveAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken);

    /// <summary>Declines a pending request.</summary>
    Task DeclineAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken);

    /// <summary>Removes a current member.</summary>
    Task RemoveMemberAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken);

    /// <summary>Super admin: adds a known player straight in as an approved member.</summary>
    Task AddMemberAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken);

    /// <summary>Super admin: makes or unmakes a group admin. The player must already be an approved member.</summary>
    Task SetAdminAsync(Guid groupChatId, Guid playerProfileId, bool isAdmin, CancellationToken cancellationToken);

    /// <summary>Finds players by a display-name fragment (at least two characters). Never phone numbers.</summary>
    Task<IReadOnlyList<PlayerSearchResultDto>> SearchPlayersAsync(string query, CancellationToken cancellationToken);
}
