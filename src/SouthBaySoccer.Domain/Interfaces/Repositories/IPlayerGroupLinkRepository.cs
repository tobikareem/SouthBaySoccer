using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SouthBaySoccer.Domain.Entities.Groups;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for <see cref="PlayerGroupLink"/> records tying player profiles to group chats.
/// </summary>
public interface IPlayerGroupLinkRepository : IRepository<PlayerGroupLink>
{
    /// <summary>
    /// Lists the active group links for a player.
    /// </summary>
    Task<IReadOnlyList<PlayerGroupLink>> ListByPlayerAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a link already exists between the player and group chat.
    /// </summary>
    Task<bool> ExistsAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the player's linked groups joined to their group-chat details, primary first.
    /// </summary>
    Task<IReadOnlyList<PlayerGroupReadModel>> ListPlayerGroupsAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default);
}

/// <summary>Projection of a player's group link joined to the group-chat display fields.</summary>
public sealed record PlayerGroupReadModel(
    Guid GroupChatId,
    string ExternalId,
    string GroupName,
    int MemberCount,
    bool IsPrimary);
