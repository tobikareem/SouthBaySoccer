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

    /// <summary>
    /// Finds the link between a player and a group, or <see langword="null"/> when none exists.
    /// The link's creation time is the floor for anything scoped to "since this player joined".
    /// </summary>
    Task<PlayerGroupLink?> FindLinkAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the players currently linked to a group, optionally excluding one profile.
    /// This is the app-side audience — the people who can actually receive an in-app broadcast —
    /// as opposed to the externally reported WhatsApp roster size.
    /// </summary>
    Task<int> CountMembersAsync(
        Guid groupChatId,
        Guid? excludingPlayerProfileId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Projection of a player's group link joined to the group-chat display fields.</summary>
public sealed record PlayerGroupReadModel(
    Guid GroupChatId,
    string ExternalId,
    string GroupName,
    int MemberCount,
    bool IsPrimary);
