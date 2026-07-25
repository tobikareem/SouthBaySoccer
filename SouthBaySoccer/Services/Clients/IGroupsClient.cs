using SouthBaySoccer.Contracts.Groups;

namespace SouthBaySoccer.Services.Clients;

/// <summary>
/// Client for WhatsApp group-chat linking. Reads and the link write both go to our own backend;
/// linkage is stored in our database (the backend only reads from Pickup Pal, never writes to it).
/// </summary>
public interface IGroupsClient
{
    /// <summary>Gets every group a player can choose from at the sign-in link step.</summary>
    Task<IReadOnlyList<GroupChatDto>> GetAvailableGroupsAsync(CancellationToken cancellationToken);

    /// <summary>Gets the current player's linked groups and whether the link step is satisfied.</summary>
    Task<MyGroupsResponse> GetMyGroupsAsync(CancellationToken cancellationToken);

    /// <summary>Links the current player to the group with the given external id.</summary>
    Task<MyGroupsResponse> LinkAsync(string groupExternalId, CancellationToken cancellationToken);
}
